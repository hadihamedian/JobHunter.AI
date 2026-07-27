using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunter.AI.Api.Models;

namespace JobHunter.AI.Api.Services;

public class InterviewBankChatService(
    HttpClient httpClient,
    IConfiguration configuration,
    InterviewRepository repository)
{
    public async Task<InterviewBankChatResponse> ChatAsync(
        InterviewBankChatRequest request)
    {
        var allQuestions = await repository.GetAllAsync();

        var questionsJson = JsonSerializer.Serialize(
            allQuestions.Select(q => new
            {
                q.Id,
                q.Question,
                q.QuestionType,
                q.Hint,
                q.Status,
                q.PersonalNote
            }),
            new JsonSerializerOptions { WriteIndented = false });

        var ollamaUrl = configuration["Ollama:Url"]
            ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "llama3.2";

        var historyText = string.Join("\n", request.History
            .Select(h => $"{h.Role}: {h.Content}"));

        var prompt = $@"You are an interview preparation assistant.

The user has this bank of interview questions:
{questionsJson}

{(string.IsNullOrEmpty(historyText) ? "" : $"Previous conversation:\n{historyText}\n")}
User question: {request.Question}

Answer the user's question about their interview questions.
You can filter, group, explain, or suggest practice strategies.

Return ONLY this JSON object:
{{
   ""textAnswer"": ""<your answer, 2-4 sentences>"",
   ""hasFilteredList"": true or false,
   ""filteredQuestionIds"": [""<uuid>"", ""<uuid>""]
}}

If the user asks to see specific questions (by topic, type, status), 
set hasFilteredList to true and include the IDs of matching questions.
Otherwise set hasFilteredList to false and filteredQuestionIds to empty array.
Always answer in the same language the user writes in.";

        var payload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0.2 }
        };

        var response = await httpClient.PostAsJsonAsync(ollamaUrl, payload);
        response.EnsureSuccessStatusCode();

        var ollamaResp = await response.Content
            .ReadFromJsonAsync<OllamaResponse>();
        if (string.IsNullOrWhiteSpace(ollamaResp?.Response))
            return new InterviewBankChatResponse("No response from AI.", null);

        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var raw = JsonSerializer.Deserialize<RawChatResult>(
                ollamaResp.Response, jsonOptions);
            if (raw == null)
                return new InterviewBankChatResponse(ollamaResp.Response, null);

            List<InterviewQuestion>? filtered = null;
            if (raw.HasFilteredList && raw.FilteredQuestionIds.Count > 0)
            {
                filtered = allQuestions
                    .Where(q => raw.FilteredQuestionIds.Contains(q.Id))
                    .ToList();
            }

            return new InterviewBankChatResponse(raw.TextAnswer, filtered);
        }
        catch
        {
            return new InterviewBankChatResponse(ollamaResp.Response, null);
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
        public bool HasFilteredList { get; set; }
        public List<Guid> FilteredQuestionIds { get; set; } = new();
    }
}