using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.AI.Api.Models;

namespace JobHunter.AI.Api.Services;

public class DataChatService(HttpClient httpClient, IConfiguration configuration, ApplicationRepository repository)
{
    public async Task<DataChatResponse?> ChatAsync(DataChatRequest request)
    {
        var applications = await repository.GetAllForChatAsync();
        var appsJson = JsonSerializer.Serialize(applications, new JsonSerializerOptions { WriteIndented = true });
        
        var url = configuration["Ollama:Url"] ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "llama3.2";
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

        var prompt = $@"You are a data analyst assistant for a job application tracker.
The user has the following job applications in their database:
{appsJson}

Today's date is {today}.

Answer the user's question about their job applications.
You MUST respond with ONLY a JSON object in this exact format:
{{
  ""textAnswer"": ""Your conversational answer here (1-3 sentences)"",
  ""hasTable"": true or false,
  ""tableColumns"": [""Column1"", ""Column2"", ""Column3""],
  ""tableRows"": [[""val1"", ""val2"", ""val3""], [""val4"", ""val5"", ""val6""]]
}}

If no table is needed, set hasTable to false and tableColumns/tableRows to empty arrays.
Always answer in the same language the user writes in (Persian or English).
Be specific and reference actual company names and dates from the data.

Question: {request.Question}";

        var requestPayload = new
        {
            model = model,
            prompt = prompt,
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
            var rawResult = JsonSerializer.Deserialize<RawChatResult>(jsonResponse.Response, options);

            if (rawResult == null) return null;

            var table = rawResult.HasTable
                ? new DataTable(rawResult.TableColumns, rawResult.TableRows)
                : null;

            return new DataChatResponse(rawResult.TextAnswer, table);
        }
        catch
        {
            // اگه JSON parse نشد، فقط متن خام رو برگردون
            return new DataChatResponse(jsonResponse.Response, null);
        }
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }

    private class RawChatResult
    {
        public string TextAnswer { get; set; } = string.Empty;
        public bool HasTable { get; set; }
        public List<string> TableColumns { get; set; } = new();
        public List<List<string>> TableRows { get; set; } = new();
    }
}