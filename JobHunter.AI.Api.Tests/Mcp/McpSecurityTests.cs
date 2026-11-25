using JobHunter.AI.Api.Mcp;
using Microsoft.Extensions.Configuration;

namespace JobHunter.AI.Api.Tests.Mcp;

public class McpSecurityTests
{
    [Theory]
    [InlineData("Origin", true)]
    [InlineData("Forwarded", true)]
    [InlineData("x-forwarded-for", true)]
    [InlineData("X-Forwarded-Host", true)]
    [InlineData("Authorization", false)]
    [InlineData("Accept", false)]
    public void RejectsBrowserAndProxyHeaders(string header, bool expected)
        => Assert.Equal(expected, McpExtensions.HasUntrustedHeaders([header]));

    private static IConfiguration Settings(string? read, string? write, bool writes, string? rest = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Token"] = read,
            ["Mcp:WriteToken"] = write,
            ["Mcp:AllowWrites"] = writes.ToString(),
            ["RestAuth:Token"] = rest
        }).Build();

    [Fact]
    public void ReadOnlyConfigurationNeedsNoWriteToken() =>
        McpExtensions.ValidateCredentials(Settings(new string('a', 64), null, false));

    [Fact]
    public void WriteEnablementRequiresSeparateCredential()
    {
        var read = new string('a', 64);
        var write = new string('b', 64);
        var rest = new string('c', 64);
        McpExtensions.ValidateCredentials(Settings(read, write, true, rest));
        Assert.Throws<InvalidOperationException>(() => McpExtensions.ValidateCredentials(Settings(read, null, true)));
        Assert.Throws<InvalidOperationException>(() => McpExtensions.ValidateCredentials(Settings(read, read, true)));
        Assert.Throws<InvalidOperationException>(() => McpExtensions.ValidateCredentials(Settings(read, write, true, write)));
        Assert.Throws<InvalidOperationException>(() => McpExtensions.ValidateCredentials(Settings(read, "not-a-token", false)));
        Assert.Throws<InvalidOperationException>(() => McpExtensions.ValidateCredentials(Settings("short", write, false)));
    }
}
