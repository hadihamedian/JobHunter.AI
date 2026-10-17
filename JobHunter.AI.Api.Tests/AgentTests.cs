using JobHunter.AI.Api.Agents;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatResponse = Microsoft.Extensions.AI.ChatResponse;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace JobHunter.AI.Api.Tests;

public class AgentTests
{
    [Fact]
    public async Task CareerAdvisorAgent_MapsHistoryRolesCorrectly()
    {
        var mockClient = new Mock<IChatClient>();
        IEnumerable<AiChatMessage>? capturedMessages = null;

        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AiChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) =>
            {
                capturedMessages = msgs.ToList();
            })
            .ReturnsAsync(new AiChatResponse(new AiChatMessage(AiChatRole.Assistant, "Here is your career advice.")));

        var skills = new CareerAdvisorTools(null!, null!);
        var agent = new CareerAdvisorAgent(mockClient.Object, skills, NullLogger<CareerAdvisorAgent>.Instance);

        var request = new CareerChatRequest(
            "What should I improve?",
            new List<CareerChatMessage>
            {
                new("user", "Hello"),
                new("assistant", "Hi, how can I help?")
            });

        var response = await agent.RunAsync(request);

        Assert.Equal("Here is your career advice.", response.Reply);
        Assert.NotNull(capturedMessages);

        var list = capturedMessages.ToList();
        Assert.Contains(list, m => m.Role == AiChatRole.System && m.Text!.Contains("expert technical career advisor"));
        Assert.Contains(list, m => m.Role == AiChatRole.User && m.Text == "Hello");
        Assert.Contains(list, m => m.Role == AiChatRole.Assistant && m.Text == "Hi, how can I help?");
        Assert.Contains(list, m => m.Role == AiChatRole.User && m.Text == "What should I improve?");
    }

    [Fact]
    public async Task DataChatAgent_ParsesJsonOutput_Correctly()
    {
        var mockClient = new Mock<IChatClient>();
        const string jsonPayload = """
            {
              "textAnswer": "You have 3 active interviews.",
              "hasTable": true,
              "tableColumns": ["Company", "Status"],
              "tableRows": [["Google", "HRInterview"]]
            }
            """;

        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatResponse(new AiChatMessage(AiChatRole.Assistant, jsonPayload)));

        var skills = new ApplicationDataTools(null!);
        var agent = new DataChatAgent(mockClient.Object, skills, NullLogger<DataChatAgent>.Instance);

        var response = await agent.RunAsync(new DataChatRequest("How many interviews?", new List<DataChatMessage>()));

        Assert.NotNull(response);
        Assert.Equal("You have 3 active interviews.", response.TextAnswer);
        Assert.NotNull(response.Table);
        Assert.Single(response.Table.Rows);
        Assert.Equal("Google", response.Table.Rows[0][0]);
    }

    [Fact]
    public async Task DataChatAgent_FallsBackToPlainText_WhenJsonFails()
    {
        var mockClient = new Mock<IChatClient>();
        const string plainText = "I could not format this as JSON, but you have 2 offers pending.";

        mockClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiChatResponse(new AiChatMessage(AiChatRole.Assistant, plainText)));

        var skills = new ApplicationDataTools(null!);
        var agent = new DataChatAgent(mockClient.Object, skills, NullLogger<DataChatAgent>.Instance);

        var response = await agent.RunAsync(new DataChatRequest("Status update?", new List<DataChatMessage>()));

        Assert.NotNull(response);
        Assert.Equal(plainText, response.TextAnswer);
        Assert.Null(response.Table);
    }
}