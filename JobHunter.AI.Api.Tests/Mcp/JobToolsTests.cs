using JobHunter.AI.Api.Mcp;
using System.Security.Claims;
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
        await Assert.ThrowsAsync<McpException>(() => JobTools.UpdateApplicationStatus(null!, configuration, Writer(), Guid.NewGuid(), "Saved", "Applied"));
    }

    [Fact]
    public async Task UpdateStatus_RejectsReadOnlyOrUnauthenticatedCallers()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:AllowWrites"] = "true" }).Build();
        foreach (var caller in new ClaimsPrincipal?[] { null, new ClaimsPrincipal(), new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, "McpReader")
        ], "McpBearer")) })
            await Assert.ThrowsAsync<McpException>(() => JobTools.UpdateApplicationStatus(null!, configuration, caller, Guid.NewGuid(), "Saved", "Applied"));
    }

    [Fact]
    public async Task UpdateStatus_RejectsInvalidStatusBeforeAccessingDatabase()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:AllowWrites"] = "true" }).Build();
        await Assert.ThrowsAsync<McpException>(() => JobTools.UpdateApplicationStatus(null!, configuration, Writer(), Guid.NewGuid(), "Saved", "invalid"));
    }

    private static ClaimsPrincipal Writer() => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "McpWriter")], "McpBearer"));

    [Fact]
    public async Task MatchingJobs_RejectsOversizedCandidateBatch()
    {
        await Assert.ThrowsAsync<McpException>(() => JobTools.GetMatchingJobs(null!, null!, null!, null!, candidateLimit: 6));
    }
    [Fact]
    public async Task MatchingBudgetReturnsCompletedResultsAndRetriesInterruptedCandidate()
    {
        using var budget = new CancellationTokenSource();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var result = await JobTools.MatchPageAsync(Guid.NewGuid(), ids, 10, (id, ct) =>
        {
            if (id == ids[1]) budget.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<(JobTools.MatchingJob?, bool)>((new(id, "Test", "Dev", null, 80), false));
        }, budget.Token, CancellationToken.None);
        Assert.True(result.TimedOut);
        Assert.Equal(1, result.CandidatesExamined);
        Assert.Equal(11, result.NextOffset);
        Assert.Equal(ids[0], Assert.Single(result.Matches).ApplicationId);
        Assert.Empty(result.SkippedApplicationIds);
    }

    [Fact]
    public async Task MatchingCallerCancellationIsNotReportedAsBudgetTimeout()
    {
        using var caller = new CancellationTokenSource();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(caller.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => JobTools.MatchPageAsync(Guid.NewGuid(), [Guid.NewGuid()], 0, (id, ct) =>
        {
            caller.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<(JobTools.MatchingJob?, bool)>((null, true));
        }, budget.Token, caller.Token));
    }
}
