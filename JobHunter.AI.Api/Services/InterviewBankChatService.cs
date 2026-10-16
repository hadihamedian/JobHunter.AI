using System.Text.Json;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Skills;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Services;

public class InterviewBankChatService(
    IChatClient chatClient,
    InterviewBankSkills skills,
    InterviewRepository repository)
{
    public async Task<InterviewBankChatResponse> ChatAsync(InterviewBankChatRequest request)
    {
        var allQuestions = await repository.GetAllAsync();

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(skills.SearchQuestionsAsync),
            AIFunctionFactory.Create(skills.GetAllQuestionsAsync)
        };

        var messages = new List<AiChatMessage>
        {
            new(ChatRole.System, """
                You are an interview question bank manager.
                Your task is to find and return question IDs from the user's database.

                RULES:
                1. NEVER define or explain technologies (never write paragraphs like "EF Core is...").
                2. If the user asks for questions on a topic, call SearchQuestionsAsync.
                3. If the user asks for more questions than exist in the bank, clearly state: "Your bank only has X question(s) for this topic."
                4. Output MUST be valid JSON only.

                JSON format:
                {
                   "textAnswer": "<1 short sentence stating how many questions exist in the bank>",
                   "hasFilteredList": true,
                   "filteredQuestionIds": ["<guid>"]
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
            Tools = tools,
            Temperature = 0.0f,
            ResponseFormat = ChatResponseFormat.Json
        };

        try
        {
            var response = await chatClient.GetResponseAsync(messages, options);
            var rawText = response.Message.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
                return new InterviewBankChatResponse("No response from AI.", null);

            rawText = CleanJsonFences(rawText);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<RawChatResult>(rawText, jsonOptions);

            if (raw is null)
                return new InterviewBankChatResponse("Could not parse AI response.", null);

            List<InterviewQuestion>? filtered = null;
            if (raw.HasFilteredList && raw.FilteredQuestionIds.Count > 0)
            {
                var idSet = raw.FilteredQuestionIds.ToHashSet();
                filtered = allQuestions.Where(q => idSet.Contains(q.Id)).ToList();
            }

            // Fallback & Guardrail: Ensure deterministic matching for topic requests
            if (filtered is null || filtered.Count == 0)
            {
                var searchToken = ExtractSearchToken(request.Question);
                if (!string.IsNullOrEmpty(searchToken))
                {
                    filtered = allQuestions.Where(q => 
                        q.Question.Contains(searchToken, StringComparison.OrdinalIgnoreCase) || 
                        (q.Hint != null && q.Hint.Contains(searchToken, StringComparison.OrdinalIgnoreCase))
                    ).ToList();
                }
            }

            // Guardrail: Intercept essay-style explanations and replace with clean count statement
            if (raw.TextAnswer.Length > 150 || raw.TextAnswer.Contains("Object-Relational", StringComparison.OrdinalIgnoreCase))
            {
                var count = filtered?.Count ?? 0;
                raw.TextAnswer = count > 0 
                    ? $"Your bank currently contains {count} question(s) for this topic:"
                    : "No matching questions were found in your bank for this topic.";
            }

            return new InterviewBankChatResponse(raw.TextAnswer, filtered);
        }
        catch
        {
            return new InterviewBankChatResponse("Error connecting to AI.", null);
        }
    }

    private static string ExtractSearchToken(string query)
    {
        var candidates = new[] { "EF Core", "Entity Framework", "SQL", "Docker", "RabbitMQ", "Microservices", "Behavioral", "C#" };
        return candidates.FirstOrDefault(c => query.Contains(c, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string CleanJsonFences(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            var lastBacktick = cleaned.LastIndexOf("```");
            if (firstNewline > 0 && lastBacktick > firstNewline)
            {
                cleaned = cleaned.Substring(firstNewline + 1, lastBacktick - firstNewline - 1).Trim();
            }
        }

        var openBrace = cleaned.IndexOf('{');
        var closeBrace = cleaned.LastIndexOf('}');
        if (openBrace >= 0 && closeBrace > openBrace)
        {
            cleaned = cleaned.Substring(openBrace, closeBrace - openBrace + 1);
        }

        return cleaned;
    }

    private class RawChatResult
    {
        public string TextAnswer { get; set; } = string.Empty;
        public bool HasFilteredList { get; set; }
        public List<Guid> FilteredQuestionIds { get; set; } = new();
    }
}