using System.Text;
using System.Text.Json.Serialization;
using JobAssistant.Api.Models;

namespace JobAssistant.Api.Services;

public class CareerChatService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public CareerChatService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request)
    {
        var url = _configuration["Ollama:Url"] ?? "http://localhost:11434/api/generate";
        var model = _configuration["Ollama:Model"] ?? "llama3.2";

        var systemPrompt = $@"You are a career coach AI assistant.

You have access to the following context:

=== RESUME ===
{request.ResumeContent}

=== JOB DESCRIPTION ===
{request.JobDescription}

=== ATS ANALYSIS RESULT ===
{request.AnalysisSummary}

Based on this context, answer the user's career-related questions.
Be specific, practical, and refer to the actual skills and gaps you see.
Keep answers concise (3-5 sentences max unless asked for more).
Always answer in the same language the user writes in.";

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(systemPrompt);
        promptBuilder.AppendLine("\n=== CONVERSATION ===");

        foreach (var msg in request.History)
        {
            var role = msg.Role == "user" ? "User" : "Assistant";
            promptBuilder.AppendLine($"{role}: {msg.Content}");
        }
        
        promptBuilder.AppendLine($"User: {request.UserMessage}");
        promptBuilder.AppendLine("Assistant:");

        var requestPayload = new
        {
            model = model,
            prompt = promptBuilder.ToString(),
            stream = false,
            options = new { temperature = 0 } // حفظ خروجی ثابت
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestPayload);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>();
        return new ChatResponse(jsonResponse?.Response?.Trim() ?? string.Empty);
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }
}