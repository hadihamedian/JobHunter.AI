using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.AI.Api.Models;

namespace JobHunter.AI.Api.Services;

public class ResumeTailorService(
    HttpClient httpClient,
    IConfiguration configuration,
    ResumeRepository resumeRepository)
{
    public async Task<TailorResult?> TailorAsync(TailorRequest request)
    {
        var resume = await resumeRepository.GetByIdAsync(request.ResumeId);
        if (resume is null) return null;

        var ollamaUrl = configuration["Ollama:Url"]
            ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "qwen2.5-coder:7b";

        var styleInstruction = request.Style == "aggressive"
            ? "Aggressively rewrite sections to maximize keyword density. Add all relevant missing keywords naturally. Reorder skills by relevance to the job."
            : "Make conservative changes only. Keep the candidate's original voice and style. Only add clearly missing keywords and make minimal rewrites.";

        var prompt = $@"You are an expert ATS resume optimizer and professional resume formatter.

Style instruction: {styleInstruction}

Original Resume (Markdown):
{resume.Content}

Job Description:
{request.JobDescription}

Task:
1. Rewrite the resume to better match the job description.
2. Keep the same overall structure and sections as the original.
3. Add relevant keywords naturally, based only on skills/experience the candidate actually has.
4. Make achievements more specific and measurable where possible.
5. Keep it truthful — never invent employers, titles, dates, or skills not present in the original.

OUTPUT FORMAT — the resume MUST be valid Markdown, following these exact rules:
- Use ""## "" for each major section title (e.g. ## Summary, ## Experience, ## Skills, ## Education).
- Use ""### "" for each job entry, formatted as: ### Job Title — Company Name (Start – End)
- Use ""- "" for every bullet point under an experience entry (max 4-5 bullets per job). Never nest bullets.
- Use ""**bold**"" only for key metrics or the technology-stack line inside Skills — not for whole sentences.
- Do NOT use tables, images, emoji, or raw HTML tags — this will be parsed by ATS systems.
- Do NOT add horizontal rules (---) between sections.
- Keep total length equivalent to 1-2 printed A4 pages.

Return ONLY this JSON object, no explanation, no markdown code fences around the JSON itself:
{{
   ""tailoredContent"": ""<the complete rewritten resume as a single Markdown string, using \\n for line breaks>"",
   ""keywordsAdded"": [""keyword1"", ""keyword2""],
   ""sectionsRewritten"": [""Summary — aligned to role"", ""Experience — added metrics""],
   ""estimatedAtsImprovement"": <number 1-30>
}}

Important: tailoredContent must be the COMPLETE resume in Markdown format, not plain text.";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0.3 }
        };

        try
        {
            var response = await httpClient.PostAsJsonAsync(ollamaUrl, payload);
            response.EnsureSuccessStatusCode();

            var ollamaResp = await response.Content
                .ReadFromJsonAsync<OllamaResponse>();
            if (string.IsNullOrWhiteSpace(ollamaResp?.Response)) return null;

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var raw = JsonSerializer.Deserialize<RawTailorResult>(
                ollamaResp.Response, jsonOptions);
            if (raw is null) return null;

            return new TailorResult(
                raw.TailoredContent,
                raw.KeywordsAdded,
                raw.SectionsRewritten,
                raw.EstimatedAtsImprovement
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

    private class RawTailorResult
    {
        public string TailoredContent { get; set; } = string.Empty;
        public List<string> KeywordsAdded { get; set; } = new();
        public List<string> SectionsRewritten { get; set; } = new();
        public int EstimatedAtsImprovement { get; set; }
    }
}