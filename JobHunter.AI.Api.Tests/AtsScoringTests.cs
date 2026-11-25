using JobHunter.AI.Api.Services;
using JobHunter.AI.Api.Tools;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace JobHunter.AI.Api.Tests;

public class AtsScoringTests
{
    [Fact]
    public async Task MissingSkillPreventsPerfectScore_AndAvoidsSecondAiCall()
    {
        var chat = new Mock<IChatClient>();
        chat.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<AiChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("""{"requirements":[{"text":"C#","type":"skill"},{"text":"React","type":"skill"},{"text":"c#","type":"skill"}]}"""));
        var result = await new AiAnalyzerService(chat.Object, new AtsEvaluationTools()).AnalyzeAsync("C# developer", "C# and React");
        Assert.NotNull(result);
        Assert.Equal(50, result.AtsScore);
        Assert.Single(result.MatchingSkills);
        Assert.Single(result.MissingSkills);
        Assert.NotEmpty(result.Suggestions);
        chat.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<AiChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task InventedExperienceAndScoreCannotOverrideExtractedRequirements()
    {
        var chat = new Mock<IChatClient>();
        chat.SetupSequence(c => c.GetResponseAsync(It.IsAny<IEnumerable<AiChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response("""{"requirements":[{"text":"C#","type":"skill"},{"text":"5 years C#","type":"experience"}]}"""))
            .ReturnsAsync(Response("""{"atsScore":100,"experienceMet":["Invented role"],"experienceNotMet":[],"criticalMissingSkills":["Invented role"],"suggestions":[]}"""));
        var result = await new AiAnalyzerService(chat.Object, new AtsEvaluationTools()).AnalyzeAsync("C# developer", "5 years C#");
        Assert.NotNull(result);
        Assert.Equal(50, result.AtsScore);
        Assert.Equal("5 years C#", Assert.Single(result.MissingSkills));
        Assert.Equal("C#", Assert.Single(result.MatchingSkills));
        Assert.Empty(result.CriticalMissingSkills);
    }

    private static AiChatResponse Response(string text) => new(new AiChatMessage(ChatRole.Assistant, text));
}
