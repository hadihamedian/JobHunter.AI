using System.Text.Json;
using System.Text.Json.Serialization;
using JobAssistant.Api.Models;

namespace JobAssistant.Api.Services;

public class InterviewGeneratorService(
    HttpClient httpClient,
    IConfiguration configuration,
    InterviewRepository repository)
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

Return ONLY this JSON array, no explanation, no markdown:
[
  {{
     ""question"": ""<the question>"",
     ""type"": ""Technical"",
     ""hint"": ""<brief guidance on how to answer this question, 1-2 sentences>""
  }},
  {{
     ""question"": ""<the question>"",
     ""type"": ""Behavioral"",
     ""hint"": ""<brief guidance on how to answer>""
  }},
  {{
     ""question"": ""<question to ask interviewer>"",
     ""type"": ""AskThem"",
     ""hint"": ""<why this is a good question to ask>""
  }}
]

Type must be exactly one of: Technical, Behavioral, AskThem
Make questions specific to the actual technologies and role mentioned.";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0.4 }
        };

        var response = await httpClient.PostAsJsonAsync(ollamaUrl, payload);
        response.EnsureSuccessStatusCode();

        var ollamaResp = await response.Content
            .ReadFromJsonAsync<OllamaResponse>();
        if (string.IsNullOrWhiteSpace(ollamaResp?.Response)) return new();

        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            List<RawQuestion>? raw = null;

            try
            {
                raw = JsonSerializer.Deserialize<List<RawQuestion>>(
                    ollamaResp.Response, jsonOptions);
            }
            catch
            {
                var wrapper = JsonSerializer.Deserialize<QuestionWrapper>(
                    ollamaResp.Response, jsonOptions);
                raw = wrapper?.Questions ?? wrapper?.Items;
            }

            if (raw == null || raw.Count == 0) return new();

            // حذف سوالات قبلی قبل از ذخیره جدید
            await repository.DeleteByApplicationAsync(req.ApplicationId);

            var toSave = raw.Select(q => (
                q.Question,
                q.Type ?? "Technical",
                q.Hint ?? ""
            )).ToList();

            await repository.SaveQuestionsAsync(req.ApplicationId, toSave);
            return await repository.GetByApplicationAsync(req.ApplicationId);
        }
        catch
        {
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
    }
}