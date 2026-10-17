using System.Text.Json;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Services;

public class InterviewGeneratorService(
    IChatClient chatClient,
    InterviewRepository repository,
    ILogger<InterviewGeneratorService> logger)
{
    public async Task<List<InterviewQuestion>> GenerateAsync(GenerateQuestionsRequest req)
    {
        var prompt = $$"""
                       You are an expert technical interviewer.
                       Generate interview questions based on the job description and resume provided.

                       Job Description:
                       {{req.JobDescription}}

                       Resume:
                       {{req.ResumeContent}}

                       Generate exactly 10 questions with this distribution:
                       - 5 Technical questions (specific to the tech stack)
                       - 3 Behavioral questions (STAR format situations)
                       - 2 Questions the candidate should ask the interviewer

                       You MUST respond with a JSON object containing a "questions" array. No explanations, no markdown fences.
                       Type must be exactly one of: "Technical", "Behavioral", "AskThem".
                       The "hint" field MUST contain 2-4 concrete technical keywords or talking points to help answer (do not use generic placeholder text).

                       {
                         "questions": [
                           {
                             "question": "Can you explain the differences between EF Core and Dapper?",
                             "type": "Technical",
                             "hint": "Performance, change tracking overhead, micro-ORM vs full ORM, raw SQL control"
                           }
                         ]
                       }
                       """;

        var messages = new List<AiChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var options = new ChatOptions
        {
            Temperature = 0.2f,
            ResponseFormat = ChatResponseFormat.Json
        };

        try
        {
            var response = await chatClient.GetResponseAsync(messages, options);
            var rawText = response.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                logger.LogWarning("LLM returned empty response for interview generation.");
                return new();
            }

            rawText = CleanJsonFences(rawText);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            List<RawQuestion>? raw = null;

            // Strategy 1: Parse as Object with "questions", "items", or "data" array
            try
            {
                var wrapper = JsonSerializer.Deserialize<QuestionWrapper>(rawText, jsonOptions);
                raw = wrapper?.Questions ?? wrapper?.Items ?? wrapper?.Data;
            }
            catch { /* proceed to next strategy */ }

            // Strategy 2: Direct Array parse
            if (raw is null || raw.Count == 0)
            {
                try
                {
                    raw = JsonSerializer.Deserialize<List<RawQuestion>>(rawText, jsonOptions);
                }
                catch { /* proceed to next strategy */ }
            }

            // Strategy 3: Single Object parse (the error case you encountered)
            if (raw is null || raw.Count == 0)
            {
                try
                {
                    var single = JsonSerializer.Deserialize<RawQuestion>(rawText, jsonOptions);
                    if (single is not null && !string.IsNullOrWhiteSpace(single.Question))
                    {
                        raw = [single];
                    }
                }
                catch { /* all strategies exhausted */ }
            }

            if (raw is null || raw.Count == 0)
            {
                logger.LogError("Failed to parse any interview questions from payload: {Raw}", rawText);
                return new();
            }

            await repository.DeleteByApplicationAsync(req.ApplicationId);

            var toSave = raw.Select(q => (
                q.Question,
                q.Type ?? "Technical",
                q.Hint ?? string.Empty
            )).ToList();

            await repository.SaveQuestionsAsync(req.ApplicationId, toSave);
            return await repository.GetByApplicationAsync(req.ApplicationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during interview question generation.");
            return new();
        }
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

        var openBrace = cleaned.IndexOfAny(['{', '[']);
        var closeBrace = cleaned.LastIndexOfAny(['}', ']']);
        if (openBrace >= 0 && closeBrace > openBrace)
        {
            cleaned = cleaned.Substring(openBrace, closeBrace - openBrace + 1);
        }

        return cleaned;
    }

    private class RawQuestion
    {
        public string Question { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Hint { get; set; }
    }

    private class QuestionWrapper
    {
        public List<RawQuestion>? Questions { get; set; }
        public List<RawQuestion>? Items { get; set; }
        public List<RawQuestion>? Data { get; set; }
    }
}