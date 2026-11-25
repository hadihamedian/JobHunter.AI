using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;
using JobHunter.AI.Api.Tools;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace JobHunter.AI.Api.Tests;

public class AtsAnalysisCacheTests
{
    private static readonly AtsCacheSettings Settings = new(true, 24,
        "http://localhost:11434", "llama3.2:latest", 8192, "");

    [Fact]
    public void Key_IsStable_AndChangesWithInputsOrModelSettings()
    {
        var key = AtsAnalysisCache.BuildKey(Settings, "C#", "Backend role");
        Assert.Equal(64, key.Length);
        Assert.Equal(key, AtsAnalysisCache.BuildKey(Settings, "C#", "Backend role"));
        Assert.NotEqual(key, AtsAnalysisCache.BuildKey(Settings, "C# updated", "Backend role"));
        Assert.NotEqual(key, AtsAnalysisCache.BuildKey(Settings, "C#", "Frontend role"));
        Assert.NotEqual(key, AtsAnalysisCache.BuildKey(Settings with { Model = "another-model" }, "C#", "Backend role"));
        Assert.NotEqual(key, AtsAnalysisCache.BuildKey(Settings with { NumCtx = 4096 }, "C#", "Backend role"));
        Assert.NotEqual(key, AtsAnalysisCache.BuildKey(Settings with { Endpoint = "http://localhost:11435" }, "C#", "Backend role"));
        Assert.NotEqual(key, AtsAnalysisCache.BuildKey(Settings with { ModelRevision = "r2" }, "C#", "Backend role"));
    }

    [Fact]
    public void Key_DoesNotConflateDifferentInputBoundaries()
    {
        Assert.NotEqual(AtsAnalysisCache.BuildKey(Settings, "ab", "c"),
            AtsAnalysisCache.BuildKey(Settings, "a", "bc"));
    }

    [Fact]
    public async Task RepeatedAnalysis_UsesCacheAndDoesNotCallAiAgain()
    {
        var chat = SuccessfulChat();
        var cache = new MemoryTestCache();
        var service = new AiAnalyzerService(chat.Object, new AtsEvaluationTools(), cache);

        var first = await service.AnalyzeAsync("C# developer", "Backend role requiring C#");
        var second = await service.AnalyzeAsync("C# developer", "Backend role requiring C#");

        Assert.NotNull(first);
        Assert.Equal(85, first.AtsScore);
        Assert.Same(first, second);
        Assert.Equal(1, cache.Stores);
        chat.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<AiChatMessage>>(),
            It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task FailedAnalysis_IsNotCached()
    {
        var chat = new Mock<IChatClient>();
        chat.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("not valid JSON"));
        var cache = new MemoryTestCache();
        var service = new AiAnalyzerService(chat.Object, new AtsEvaluationTools(), cache);
        Assert.Null(await service.AnalyzeAsync("C#", "Backend role"));
        Assert.Equal(0, cache.Stores);
    }

    [Fact]
    public async Task DisabledCache_DoesNotAccessCache()
    {
        var chat = SuccessfulChat();
        var cache = new Mock<IAtsAnalysisCache>(MockBehavior.Strict);
        cache.SetupGet(c => c.Enabled).Returns(false);
        var service = new AiAnalyzerService(chat.Object, new AtsEvaluationTools(), cache.Object);
        Assert.NotNull(await service.AnalyzeAsync("C# developer", "Backend role"));
        cache.VerifyGet(c => c.Enabled, Times.Once);
        cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelledRequest_DoesNotAccessCacheOrAi()
    {
        var chat = new Mock<IChatClient>(MockBehavior.Strict);
        var cache = new Mock<IAtsAnalysisCache>(MockBehavior.Strict);
        var service = new AiAnalyzerService(chat.Object, new AtsEvaluationTools(), cache.Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AnalyzeAsync("C#", "Backend role", cts.Token));
        chat.VerifyNoOtherCalls();
        cache.VerifyNoOtherCalls();
    }

    [Fact]
    public void InvalidScoresAndNullResults_AreNotCacheable()
    {
        Assert.False(AtsAnalysisCache.IsCacheable(null));
        Assert.False(AtsAnalysisCache.IsCacheable(new AnalyzeResult(101, [], [], [], [])));
        Assert.False(AtsAnalysisCache.IsCacheable(new AnalyzeResult(80, null!, [], [], [])));
        Assert.True(AtsAnalysisCache.IsCacheable(new AnalyzeResult(80, [], [], [], [])));
    }

    private static Mock<IChatClient> SuccessfulChat()
    {
        var chat = new Mock<IChatClient>();
        chat.SetupSequence(c => c.GetResponseAsync(It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("""{"requirements":[{"text":"C#","type":"skill"}]}"""))
            .ReturnsAsync(Response("""{"atsScore":85,"experienceMet":[],"experienceNotMet":[],"criticalMissingSkills":[],"suggestions":["Review role"]}"""));
        return chat;
    }

    private static AiChatResponse Response(string text) => new(new AiChatMessage(ChatRole.Assistant, text));

    private sealed class MemoryTestCache : IAtsAnalysisCache
    {
        private readonly Dictionary<string, AnalyzeResult> _entries = new();
        public bool Enabled => true;
        public int Stores { get; private set; }
        public string CreateKey(string resume, string jobDescription) => AtsAnalysisCache.BuildKey(Settings, resume, jobDescription);
        public Task<AnalyzeResult?> TryGetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult<AnalyzeResult?>(_entries.GetValueOrDefault(key));
        public Task TryStoreAsync(string key, AnalyzeResult result, CancellationToken cancellationToken)
        {
            _entries[key] = result;
            Stores++;
            return Task.CompletedTask;
        }
    }
}
