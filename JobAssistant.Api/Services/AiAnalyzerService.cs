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