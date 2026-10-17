using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AppChatResponse = JobHunter.AI.Api.Models.ChatResponse;

namespace JobHunter.AI.Api.Services;

public class CareerChatService(IChatClient chatClient, ILogger<CareerChatService> logger)
{
    private const int ResumeBudget = 4000;
    private const int JobDescBudget = 3000;

    public async Task<AppChatResponse> ChatAsync(ChatRequest request)
    {
        var resume = TruncateWithWarning(request.ResumeContent, ResumeBudget, "Resume");
        var jobDesc = TruncateWithWarning(request.JobDescription, JobDescBudget, "JobDescription");

        var systemPrompt = $$"""
            You are an expert career coach AI assistant.
            You have access to the following context:

            === RESUME ===
            {{resume}}

            === JOB DESCRIPTION ===
            {{jobDesc}}

            === ATS ANALYSIS RESULT ===
            {{request.AnalysisSummary}}

            Based on this context, answer the user's career-related questions.
            Be specific, practical, and refer to the actual skills and gaps identified.
            Keep answers concise (3-5 sentences unless asked for more details).
            Always answer in the exact same language the user writes in (English or Persian).
            """;

        var messages = new List<AiChatMessage>
        {
            new(ChatRole.System, systemPrompt)
        };

        foreach (var msg in request.History)
        {
            var role = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new AiChatMessage(role, msg.Content));
        }

        messages.Add(new AiChatMessage(ChatRole.User, request.UserMessage));

        var options = new ChatOptions
        {
            Temperature = 0.2f
        };

        try
        {
            var response = await chatClient.GetResponseAsync(messages, options);
            return new AppChatResponse(response.Text.Trim());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error communicating with chat client in CareerChatService.");
            return new AppChatResponse("Error connecting to AI career coach.");
        }
    }

    private string TruncateWithWarning(string text, int budget, string fieldName)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= budget)
            return text;

        logger.LogWarning("{Field} exceeded token budget of {Budget} chars ({Length} chars). Truncating.", fieldName, budget, text.Length);
        return string.Concat(text.AsSpan(0, budget), "\n[truncated]");
    }
}