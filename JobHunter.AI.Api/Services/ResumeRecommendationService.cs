using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Services;

public class ResumeRecommendationService(
    IChatClient chatClient,
    ResumeRepository resumeRepository,
    ILogger<ResumeRecommendationService> logger)
{
    public async Task<ResumeRecommendation?> RecommendAsync(string jobDescription)
    {
        var resumes = await resumeRepository.GetAllWithContentAsync();
        if (resumes.Count == 0)
        {
            logger.LogWarning("No resumes found in database to evaluate.");
            return null;
        }

        var resumeList = string.Join("\n\n", resumes.Select((r, i) =>
            $"RESUME {i + 1} — ID: {r.Id} — Name: {r.Name}\n{r.Content}"));

        var prompt = $$"""
            You are a resume matching expert.
            The candidate has the following resumes:

            {{resumeList}}

            Job Description:
            {{jobDescription}}

            Compare each resume against the job description.
            Return ONLY a valid JSON object matching this schema:
            {
               "bestResumeId": "<exact id from above>",
               "bestResumeName": "<exact name from above>",
               "bestScore": <number 0-100>,
               "reasoning": "<2-3 sentences explaining why this resume is best>",
               "allScores": [
                 { "resumeId": "<exact id from above>", "resumeName": "<exact name from above>", "score": <number 0-100> }
               ]
            }

            allScores must include ALL candidate resumes sorted by score descending.
            """;

        var messages = new List<AiChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var options = new ChatOptions
        {
            Temperature = 0.0f,
            ResponseFormat = ChatResponseFormat.Json
        };

        try
        {
            var response = await chatClient.GetResponseAsync(messages, options);
            var rawText = response.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                logger.LogWarning("LLM returned empty response for resume recommendation.");
                return null;
            }

            rawText = CleanJsonFences(rawText);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<RawRecommendation>(rawText, jsonOptions);
            if (raw is null)
            {
                logger.LogWarning("Failed to deserialize LLM response: {RawText}", rawText);
                return null;
            }

            // Safe deterministic ID mapping: Match IDs to real database records
            var resolvedScores = new List<ResumeScore>();
            foreach (var s in raw.AllScores)
            {
                var matched = resumes.FirstOrDefault(r => 
                    r.Id.ToString().Equals(s.ResumeId, StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Equals(s.ResumeName, StringComparison.OrdinalIgnoreCase));

                var realId = matched?.Id ?? (Guid.TryParse(s.ResumeId, out var parsedGuid) ? parsedGuid : resumes.First().Id);
                var realName = matched?.Name ?? s.ResumeName;

                resolvedScores.Add(new ResumeScore(realId, realName, s.Score));
            }

            // Resolve best resume ID
            var bestMatched = resumes.FirstOrDefault(r =>
                r.Id.ToString().Equals(raw.BestResumeId, StringComparison.OrdinalIgnoreCase) ||
                r.Name.Equals(raw.BestResumeName, StringComparison.OrdinalIgnoreCase)) 
                ?? resolvedScores.OrderByDescending(s => s.Score).Select(s => resumes.First(r => r.Id == s.ResumeId)).FirstOrDefault()
                ?? resumes.First();

            return new ResumeRecommendation(
                bestMatched.Id,
                bestMatched.Name,
                raw.BestScore,
                raw.Reasoning,
                resolvedScores.OrderByDescending(s => s.Score).ToList()
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during resume recommendation.");
            return null;
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

        var openBrace = cleaned.IndexOf('{');
        var closeBrace = cleaned.LastIndexOf('}');
        if (openBrace >= 0 && closeBrace > openBrace)
        {
            cleaned = cleaned.Substring(openBrace, closeBrace - openBrace + 1);
        }

        return cleaned;
    }

    private class RawRecommendation
    {
        public string BestResumeId { get; set; } = string.Empty;
        public string BestResumeName { get; set; } = string.Empty;
        public int BestScore { get; set; }
        public string Reasoning { get; set; } = string.Empty;
        public List<RawScore> AllScores { get; set; } = new();
    }

    private class RawScore
    {
        public string ResumeId { get; set; } = string.Empty;
        public string ResumeName { get; set; } = string.Empty;
        public int Score { get; set; }
    }
}