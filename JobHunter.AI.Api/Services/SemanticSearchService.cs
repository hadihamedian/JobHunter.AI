using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.AI.Api.Models;

namespace JobHunter.AI.Api.Services;

public class SemanticSearchService(HttpClient httpClient, IConfiguration configuration, ApplicationRepository repository)
{
    public async Task<SemanticSearchResponse?> SearchAsync(string query)
    {
        var applications = await repository.GetAllLiteAsync();
        if (applications.Count == 0)
            return new SemanticSearchResponse(new List<Guid>(), "No applications yet.");

        // فقط فیلدهای لازم رو می‌فرستیم، Notes رو هم کوتاه می‌کنیم تا پرامپت سنگین نشه
        var compact = applications.Select(a => new
        {
            id = a.Id,
            company = a.CompanyName,
            position = a.Position,
            source = a.Source,
            status = a.Status,
            atsScore = a.AtsScore,
            appliedAt = a.AppliedAt?.ToString("yyyy-MM-dd"),
            notes = string.IsNullOrWhiteSpace(a.Notes) ? null : (a.Notes.Length > 200 ? a.Notes[..200] : a.Notes)
        });
        var appsJson = JsonSerializer.Serialize(compact, new JsonSerializerOptions { WriteIndented = true });

        var url = configuration["Ollama:Url"] ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "llama3.2";
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

        var prompt = $@"You are a semantic search assistant for a job application tracker.
Below is the user's full list of job applications as JSON. Each item has an ""id"" field.
{appsJson}

Today's date is {today}.

The user typed this search query (Persian or English, could be a natural-language description, not an exact keyword):
""{query}""

Decide which applications from the list above match the user's intent. Consider status, source, applied date, ATS score, and notes.
You MUST respond with ONLY a JSON object in this exact format:
{{
  ""matchingIds"": [""id1"", ""id2""],
  ""explanation"": ""short 1-sentence explanation of what you matched on, in the same language as the query""
}}

Rules:
- Only use ""id"" values that appear in the list above. Never invent an id.
- If nothing matches, return an empty matchingIds array with a short explanation.
- Do not include any text outside the JSON object.";

        var requestPayload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0 }
        };

        var response = await httpClient.PostAsJsonAsync(url, requestPayload);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>();
        if (string.IsNullOrWhiteSpace(jsonResponse?.Response)) return null;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        try
        {
            var raw = JsonSerializer.Deserialize<RawSemanticResult>(jsonResponse.Response, options);
            if (raw == null) return null;

            // لایه‌ی دیترمینیستیک: فقط IDهایی که واقعاً وجود دارن قبول می‌شن، مدل نمی‌تونه چیزی اختراع کنه
            var validIds = applications.Select(a => a.Id).ToHashSet();
            var safeIds = raw.MatchingIds
                .Where(idStr => Guid.TryParse(idStr, out _))
                .Select(Guid.Parse)
                .Where(id => validIds.Contains(id))
                .ToList();

            return new SemanticSearchResponse(safeIds, raw.Explanation);
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

    private class RawSemanticResult
    {
        public List<string> MatchingIds { get; set; } = new();
        public string Explanation { get; set; } = string.Empty;
    }
}