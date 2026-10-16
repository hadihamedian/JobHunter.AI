using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AppChatResponse = JobHunter.AI.Api.Models.ChatResponse;

namespace JobHunter.AI.Api.Services;

public class CareerChatService(IChatClient chatClient)
{
    public async Task<AppChatResponse> ChatAsync(ChatRequest request)
    {
        var systemPrompt = $$"""
                             You are an expert career coach AI assistant.
                             You have access to the following context:

                             === RESUME ===
                             {{request.ResumeContent}}

                             === JOB DESCRIPTION ===
                             {{request.JobDescription}}

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
            return new AppChatResponse(response.Message.Text?.Trim() ?? string.Empty);
        }
        catch
        {
            return new AppChatResponse("Error connecting to AI career coach.");
        }
    }
}