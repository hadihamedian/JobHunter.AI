using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Skills;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Services;

public class CareerAdvisorService(
    IChatClient chatClient,
    CareerAdvisorSkills skills)
{
    public async Task<CareerChatResponse> ChatAsync(CareerChatRequest request)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(skills.GetCandidateResumesAsync),
            AIFunctionFactory.Create(skills.GetJobSearchStatsAsync),
            AIFunctionFactory.Create(skills.GetRecentApplicationsAsync)
        };

        var messages = new List<AiChatMessage>
        {
            new(ChatRole.System, """
                You are a senior technical career advisor and mentor for software engineers.
                You have access to tools that retrieve the candidate's resumes, application records, and career metrics.

                Guidelines:
                - Give personalized, direct, and actionable advice.
                - When asked about strengths, weaknesses, or readiness, inspect the candidate's resumes using the provided tools.
                - Keep answers focused (3-5 sentences unless detailed strategic advice is requested).
                - Always answer in the exact same language as the user (Persian or English).
                """)
        };

        foreach (var historyMsg in request.History)
        {
            var role = historyMsg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new AiChatMessage(role, historyMsg.Content));
        }

        messages.Add(new AiChatMessage(ChatRole.User, request.UserMessage));

        var options = new ChatOptions
        {
            Tools = tools,
            Temperature = 0.4f
        };

        try
        {
            var response = await chatClient.GetResponseAsync(messages, options);
            return new CareerChatResponse(response.Message.Text?.Trim() ?? "No response from advisor.");
        }
        catch (Exception ex)
        {
            return new CareerChatResponse($"Error: {ex.Message}");
        }
    }
}