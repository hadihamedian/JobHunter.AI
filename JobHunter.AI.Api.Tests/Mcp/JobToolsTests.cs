using JobHunter.AI.Api.Mcp;
using Microsoft.Extensions.Configuration;
using Xunit;
using ModelContextProtocol;

namespace JobHunter.AI.Api.Tests.Mcp;

public class JobToolsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(101, 0)]
    [InlineData(10, -1)]
    [InlineData(10, 100001)]
    public void SearchJobs_RejectsInvalidPagination(int limit, int offset)
    {
        Assert.Throws<McpException>(() => { _ = JobTools.SearchJobs(null!, limit: limit, offset: offset); });
    }

    [Fact]
    public void SearchJobs_RejectsInvalidStatus()
    {
        Assert.Throws<McpException>(() => { _ = JobTools.SearchJobs(null!, status: "invalid"); });
    }

    [Fact]
    public void SearchJobs_RejectsOversizedQuery()
    {
        Assert.Throws<McpException>(() => { _ = JobTools.SearchJobs(null!, query: new string('x', 301)); });
    }

    [Fact]
    public async Task UpdateStatus_DeniesWritesByDefault()
    {
        var configuration = new ConfigurationBuilder().Build();
        await Assert.ThrowsAsync<McpException>(() => JobTools.UpdateApplicationStatus(null!, configuration, Guid.NewGuid(), "Saved", "Applied"));
    }

    [Fact]
    public async Task UpdateStatus_RejectsInvalidStatusBeforeAccessingDatabase()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:AllowWrites"] = "true" }).Build();
        await Assert.ThrowsAsync<McpException>(() => JobTools.UpdateApplicationStatus(null!, configuration, Guid.NewGuid(), "Saved", "invalid"));
    }

    [Fact]
    public async Task MatchingJobs_RejectsOversizedCandidateBatch()
    {
        await Assert.ThrowsAsync<McpException>(() => JobTools.GetMatchingJobs(null!, null!, null!, candidateLimit: 6));
    }
}
