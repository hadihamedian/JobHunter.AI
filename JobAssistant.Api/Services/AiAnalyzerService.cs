using System.Text.Json;
using System.Text.Json.Serialization;
using JobAssistant.Api.Models;

namespace JobAssistant.Api.Services;

public class AiAnalyzerService(HttpClient httpClient, IConfiguration configuration)
{
    public async Task<AnalyzeResult?> AnalyzeAsync(string resume, string jobDesc)
    {
        var url = configuration["Ollama:Url"] ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "llama3.2";
        
        var prompt = $@"You are an ATS (Applicant Tracking System) analyzer.
Analyze the resume against the job description and return ONLY a JSON object.

Resume:
{resume}

Job Description:
{jobDesc}

CLASSIFICATION RULES (follow strictly):
1. A skill/requirement is CRITICAL only if the job description presents it as
   mandatory — phrases like ""required"", ""must have"", ""X+ years experience
   in"", or it appears as a bare requirement with no qualifying language.
2. A skill is NOT critical (treat as ""nice to have"" -> goes to missingSkills,
   NOT criticalMissingSkills) if the JD hedges it with any of:
   - ""plus"", ""nice to have"", ""preferred"", ""bonus"", ""a plus would be""
   - an ""or"" / ""such as"" list where only ONE of several options is needed
     (e.g. ""AWS, Azure, or GCP"" -> if the candidate has ANY one of these,
     none of them are missing skills at all)
   - a dedicated ""Nice to Have"" / ""Preferred Qualifications"" section
3. When a requirement is an ""or"" choice (one-of-many), check the resume for
   ANY matching option. If the candidate has at least one, mark the whole
   group as matched in matchingSkills and do not list the others as missing.
4. Do not duplicate the same underlying requirement across matchingSkills,
   missingSkills, and criticalMissingSkills.
5. If the job description emphasizes a specific domain focus (e.g. AI/agentic
   tooling, a particular architecture style) and the resume shows hands-on
   experience in that exact focus, note this as a matching strength even if
   the exact wording differs.

Return ONLY this JSON, no explanation, no markdown:
{{
  ""atsScore"": <number 0-100>,
  ""matchingSkills"": [""skill1"", ""skill2""],
  ""missingSkills"": [""skill1"", ""skill2""],
  ""criticalMissingSkills"": [""skill1""],
  ""suggestions"": [
    ""Add measurable achievements to your experience section"",
    ""Mention Docker explicitly in your skills""
  ]
}}";

        var requestPayload = new
        {
            model = model,
            prompt = prompt,
            stream = false,
            format = "json",
            options = new 
            { 
                temperature = 0 
            }
        };

        var response = await httpClient.PostAsJsonAsync(url, requestPayload);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>();
        if (string.IsNullOrWhiteSpace(jsonResponse?.Response)) return null;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<AnalyzeResult>(jsonResponse.Response, options);
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }
}