using System.Text.Json;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Tools;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Services;

public class InterviewBankChatService(
    IChatClient chatClient,
    InterviewBankTools tools,
    ILogger<InterviewBankChatService> logger)
{
    public async Task<InterviewBankChatResponse> ChatAsync(InterviewBankChatRequest request)
    {
        var messages = new List<AiChatMessage>
        {
            new(ChatRole.System, """
                You are an interview question query parser.
                Extract the target topic, technology, or keyword the user is asking about (e.g. "EF Core", "SQL", "Behavioral").
                If the user asks for all questions or no topic is specified, leave keyword empty.
                If the user requested a specific quantity, extract that number into requestedCount.

                Respond ONLY in JSON:
                {
                   "keyword": "<extracted keyword or empty string>",
                   "requestedCount": <integer or null>
                }
                """)
        };

        foreach (var historyMsg in request.History)
        {
            var role = historyMsg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new AiChatMessage(role, historyMsg.Content));
        }

        messages.Add(new AiChatMessage(ChatRole.User, request.Question));

        var options = new ChatOptions
        {
            Temperature = 0.0f,
            ResponseFormat = ChatResponseFormat.Json
        };

        var keyword = string.Empty;
        int? requestedCount = null;

        try
        {
            var response = await chatClient.GetResponseAsync(messages, options);
            var rawText = JsonResponseCleaner.Clean(response.Text);

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                using var doc = JsonDocument.Parse(rawText);
                if (doc.RootElement.TryGetProperty("keyword", out var kwProp))
                    keyword = kwProp.GetString()?.Trim() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("requestedCount", out var countProp) && countProp.TryGetInt32(out var parsedCount))
                    requestedCount = parsedCount;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse topic from user query. Falling back to query keyword search.");
        }

        var searchTopic = string.IsNullOrWhiteSpace(keyword) ? request.Question.Trim() : keyword;
        var filtered = await tools.SearchQuestionsAsync(searchTopic);

        string textAnswer;
        if (filtered.Count == 0)
        {
            textAnswer = string.IsNullOrWhiteSpace(keyword)
                ? "No matching questions were found in your bank."
                : $"No questions found in your bank matching \"{keyword}\".";
        }
        else if (requestedCount.HasValue && requestedCount.Value > filtered.Count)
        {
            textAnswer = $"Your bank only has {filtered.Count} question(s) matching \"{searchTopic}\" (you asked for {requestedCount.Value}).";
        }
        else
        {
            textAnswer = $"Found {filtered.Count} question(s) matching \"{searchTopic}\" in your bank:";
        }

        return new InterviewBankChatResponse(textAnswer, filtered.Count > 0 ? filtered : null);
    }
}