using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Skills;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Agents;

public class CareerAdvisorAgent
{
    private readonly ChatClientAgent _agent;
    private readonly ILogger<CareerAdvisorAgent> _logger;

    public const string SystemInstructions = """
        You are an expert technical career advisor and mentor for software engineers.
        You have direct access to tools that retrieve candidate resumes, job applications, and career search metrics.
        - Give personalized, direct, and actionable advice.
        - Always query the provided tools when asked about specific profile details, metrics, or resumes.
        - Keep answers focused (3-5 sentences unless detailed strategic plans are requested).
        - Always answer in the exact same language the user writes in (Persian or English).
        """;

    public CareerAdvisorAgent(IChatClient chatClient, CareerAdvisorSkills skills, ILogger<CareerAdvisorAgent> logger)
    {
        _logger = logger;
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(skills.GetCandidateResumesAsync),
            AIFunctionFactory.Create(skills.GetResumeContentAsync),
            AIFunctionFactory.Create(skills.GetJobSearchStatsAsync),
            AIFunctionFactory.Create(skills.GetRecentApplicationsAsync)
        };

        _agent = new ChatClientAgent(chatClient, name: "CareerAdvisor", instructions: SystemInstructions, tools: tools);
    }

    public async Task<CareerChatResponse> RunAsync(CareerChatRequest request, CancellationToken cancellationToken = default)
    {
        var messages = new List<AiChatMessage>();

        foreach (var historyMsg in request.History)
        {
            var role = historyMsg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new AiChatMessage(role, historyMsg.Content));
        }

        messages.Add(new AiChatMessage(ChatRole.User, request.UserMessage));

        try
        {
            var response = await _agent.RunAsync(messages, session: null, cancellationToken: cancellationToken);
            var reply = response.Text?.Trim() ?? "No response from advisor.";
            return new CareerChatResponse(reply);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CareerAdvisorAgent.");
            return new CareerChatResponse($"Error: {ex.Message}");
        }
    }
}