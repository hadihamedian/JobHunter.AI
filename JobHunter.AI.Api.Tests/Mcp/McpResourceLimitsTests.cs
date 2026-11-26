using JobHunter.AI.Api.Mcp;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;

namespace JobHunter.AI.Api.Tests.Mcp;

public class McpResourceLimitsTests
{
    private static IConfiguration Settings(params (string Name, string Value)[] entries) =>
        new ConfigurationBuilder().AddInMemoryCollection(entries.ToDictionary(
            e => "Mcp:Limits:" + e.Name, e => (string?)e.Value)).Build();

    [Fact]
    public void RequestBudgetDoesNotRefundCompletedCalls()
    {
        using var limits = new McpResourceLimits(Settings(("RequestsPerMinute", "1")));
        using (var first = limits.TryRequest()) Assert.True(first.IsAcquired);
        using var denied = limits.TryRequest();
        Assert.False(denied.IsAcquired);
    }

    [Fact]
    public void ConcurrentTransportSlotsAreReleased()
    {
        using var limits = new McpResourceLimits(Settings(("MaxConcurrentRequests", "1")));
        using (var first = limits.TryTransport())
        {
            Assert.True(first.IsAcquired);
            using var denied = limits.TryTransport();
            Assert.False(denied.IsAcquired);
        }
        using var next = limits.TryTransport();
        Assert.True(next.IsAcquired);
    }

    [Fact]
    public void AnalysisConcurrencyAndRateAreIndependent()
    {
        using var limits = new McpResourceLimits(Settings(("AnalysisPerMinute", "2")));
        using (var first = limits.AcquireAnalysis())
            Assert.Throws<McpException>(() => limits.AcquireAnalysis());
        using (var second = limits.AcquireAnalysis()) Assert.True(second.IsAcquired);
        Assert.Throws<McpException>(() => limits.AcquireAnalysis());
    }

    [Theory]
    [InlineData("RequestsPerMinute", "0")]
    [InlineData("AnalysisPerMinute", "121")]
    [InlineData("MaxConcurrentRequests", "33")]
    [InlineData("MaxBodyBytes", "1023")]
    public void InvalidConfigurationFailsClosed(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => new McpResourceLimits(Settings((key, value))));
}
