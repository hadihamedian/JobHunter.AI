using System.Text.Json;
using System.Text.Json.Serialization;
using JobAssistant.Api.Models;

namespace JobAssistant.Api.Services;

public class ResumeRecommendationService(
    HttpClient httpClient,
    IConfiguration configuration,
    ResumeRepository resumeRepository)
{
    public async Task<ResumeRecommendation?> RecommendAsync(string jobDescription)
    {
        var resumes = await resumeRepository.GetAllWithContentAsync();
        if (resumes.Count == 0) return null;

        var ollamaUrl = configuration["Ollama:Url"] ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "llama3.2";

        var resumeList = string.Join("\n\n", resumes.Select((r, i) =>
            $"RESUME {i + 1} — ID: {r.Id} — Name: {r.Name}\n{r.Content}"));

        var prompt = $@"You are a resume matching expert.

The candidate has these resumes:

{resumeList}

Job Description:
{jobDescription}

Compare each resume against the job description.
Return ONLY this JSON object, no explanation, no markdown:
{{
   ""bestResumeId"": ""<uuid of best resume>"",
   ""bestResumeName"": ""<name of best resume>"",
   ""bestScore"": <0-100>,
   ""reasoning"": ""<2-3 sentences explaining why this resume is best and what it has that others don't>"",
   ""allScores"": [
    {{ ""resumeId"": ""<uuid>"", ""resumeName"": ""<name>"", ""score"": <0-100> }}
  ]
}}

Score based on: keyword match, relevant experience, skills alignment.
allScores must include ALL resumes sorted by score descending.";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0 }
        };

        var response = await httpClient.PostAsJsonAsync(ollamaUrl, payload);
        response.EnsureSuccessStatusCode();

        var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>();
        if (string.IsNullOrWhiteSpace(ollamaResponse?.Response)) return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<RawRecommendation>(ollamaResponse.Response, options);
            if (raw == null) return null;

            return new ResumeRecommendation(
                raw.BestResumeId,
                raw.BestResumeName,
                raw.BestScore,
                raw.Reasoning,
                raw.AllScores.Select(s =>
                    new ResumeScore(s.ResumeId, s.ResumeName, s.Score)).ToList()
            );
        }
        catch
        {
            return null;
        }
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }

    private class RawRecommendation
    {
        public Guid BestResumeId { get; set; }
        public string BestResumeName { get; set; } = string.Empty;
        public int BestScore { get; set; }
        public string Reasoning { get; set; } = string.Empty;
        public List<RawScore> AllScores { get; set; } = new();
    }

    private class RawScore
    {
        public Guid ResumeId { get; set; }
        public string ResumeName { get; set; } = string.Empty;
        public int Score { get; set; }
    }
}