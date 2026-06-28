using System.Text.Json;
using System.Text.Json.Serialization;
using JobAssistant.Api.Models;

namespace JobAssistant.Api.Services;

public class InterviewGeneratorService(
    HttpClient httpClient,
    IConfiguration configuration,
    InterviewRepository repository,
    ILogger<InterviewGeneratorService> logger)
{
    public async Task<List<InterviewQuestion>> GenerateAsync(
        GenerateQuestionsRequest req)
    {
        var ollamaUrl = configuration["Ollama:Url"]
            ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "llama3.2";

        var prompt = $@"You are an expert technical interviewer.

Based on this job description and resume, generate interview questions.

Job Description:
{req.JobDescription}

Resume:
{req.ResumeContent}

Generate exactly 10 questions in this mix:
- 5 Technical questions (specific to the tech stack in the job)
- 3 Behavioral questions (STAR format situations)
- 2 Questions the candidate should ask the interviewer

Return ONLY a JSON array, no explanation, no markdown, no code blocks.
Each item must have: question, type, hint.
Type must be exactly one of: Technical, Behavioral, AskThem.

[
  {{""question"": ""..."", ""type"": ""Technical"", ""hint"": ""...""}},
  ...
]";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            options = new { temperature = 0.4 }
        };

        try
        {
            var response = await httpClient.PostAsJsonAsync(ollamaUrl, payload);
            response.EnsureSuccessStatusCode();

            var ollamaResp = await response.Content
                .ReadFromJsonAsync<OllamaResponse>();

            if (string.IsNullOrWhiteSpace(ollamaResp?.Response))
            {
                logger.LogWarning("Ollama returned empty response");
                return new();
            }

            logger.LogInformation("Ollama response length: {Length}",
                ollamaResp.Response.Length);
            logger.LogDebug("Raw response: {Response}",
                ollamaResp.Response.Substring(0, Math.Min(500, ollamaResp.Response.Length)));

            // Strip markdown code blocks if present
            var rawText = ollamaResp.Response.Trim();
            if (rawText.StartsWith("```"))
            {
                var firstNewline = rawText.IndexOf('\n');
                var lastBacktick = rawText.LastIndexOf("```");
                if (firstNewline > 0 && lastBacktick > firstNewline)
                {
                    rawText = rawText.Substring(firstNewline + 1,
                        lastBacktick - firstNewline - 1).Trim();
                }
            }

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            List<RawQuestion>? raw = null;

            // Try 1: Direct array
            try
            {
                raw = JsonSerializer.Deserialize<List<RawQuestion>>(rawText, jsonOptions);
                logger.LogInformation("Parsed as direct array, count: {Count}", raw?.Count ?? 0);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse as direct array");
            }

            // Try 2: Wrapper object with "questions" or "items" key
            if (raw == null || raw.Count == 0)
            {
                try
                {
                    var wrapper = JsonSerializer.Deserialize<QuestionWrapper>(rawText, jsonOptions);
                    raw = wrapper?.Questions ?? wrapper?.Items;
                    logger.LogInformation("Parsed as wrapper, count: {Count}", raw?.Count ?? 0);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse as wrapper");
                }
            }

            // Try 3: Wrapper with "data" key
            if (raw == null || raw.Count == 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawText);
                    if (doc.RootElement.TryGetProperty("data", out var dataEl))
                    {
                        raw = JsonSerializer.Deserialize<List<RawQuestion>>(
                            dataEl.GetRawText(), jsonOptions);
                        logger.LogInformation("Parsed from 'data' key, count: {Count}", raw?.Count ?? 0);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse from 'data' key");
                }
            }

            if (raw == null || raw.Count == 0)
            {
                logger.LogError("Failed to parse any questions from response");
                return new();
            }

            // Delete old questions before saving new ones
            await repository.DeleteByApplicationAsync(req.ApplicationId);

            var toSave = raw.Select(q => (
                q.Question,
                q.Type ?? "Technical",
                q.Hint ?? ""
            )).ToList();

            await repository.SaveQuestionsAsync(req.ApplicationId, toSave);
            return await repository.GetByApplicationAsync(req.ApplicationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in GenerateAsync");
            return new();
        }
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
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