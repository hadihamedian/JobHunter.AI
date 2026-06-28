using System.Text.Json.Serialization;
using JobAssistant.Api.Models;

namespace JobAssistant.Api.Services;

public class CareerAdvisorService(
    HttpClient httpClient,
    IConfiguration configuration,
    ResumeRepository resumeRepository,
    ApplicationRepository applicationRepository)
{
    public async Task<CareerChatResponse> ChatAsync(CareerChatRequest request)
    {
        var ollamaUrl = configuration["Ollama:Url"]
            ?? "http://localhost:11434/api/generate";
        var model = configuration["Ollama:Model"] ?? "qwen2.5-coder:7b";

        // گرفتن همه داده‌ها
        var resumes = await resumeRepository.GetAllWithContentAsync();
        var applications = await applicationRepository.GetAllAsync();
        var stats = await applicationRepository.GetStatsAsync();

        // ساخت context رزومه‌ها
        var resumeContext = resumes.Count == 0
            ? "No resumes uploaded yet."
            : string.Join("\n\n", resumes.Select(r =>
                $"Resume: {r.Name}\n{r.Content}"));

        // ساخت context اپلیکیشن‌ها
        var appContext = applications.Count == 0
            ? "No applications yet."
            : string.Join("\n", applications.Select(a =>
                $"- {a.CompanyName} | {a.Position} | {a.Status} | " +
                $"{a.AppliedAt?.ToString("MMM d") ?? a.CreatedAt.ToString("MMM d")} | " +
                $"Source: {a.Source}"));

        // آمار کلی
        var statsContext = $@"Total applications: {stats.TotalApplications}
Active interviews: {stats.ActiveInterviews}
Response rate: {stats.ResponseRate}%
Rejected this month: {stats.RejectedThisMonth}
Applied this month: {stats.AppliedThisMonth}";

        // تاریخچه مکالمه
        var historyText = request.History.Count == 0
            ? ""
            : "Previous conversation:\n" + string.Join("\n",
                request.History.Select(h => $"{h.Role}: {h.Content}")) + "\n\n";

        var systemPrompt = $@"You are an expert career advisor for software engineers.

You have full access to the candidate's career data:

=== RESUMES ===
{resumeContext}

=== JOB APPLICATIONS ===
{appContext}

=== CAREER STATS ===
{statsContext}

Based on this data, give personalized, specific career advice.
Be direct, actionable, and reference actual data from their profile.
Keep answers focused (3-5 sentences unless more detail is needed).
Always answer in the same language the user writes in (Persian or English).

{historyText}User: {request.UserMessage}
Assistant: ";

        var payload = new
        {
            model,
            prompt = systemPrompt,
            stream = false,
            options = new { temperature = 0.5 }
        };

        try
        {
            var response = await httpClient.PostAsJsonAsync(ollamaUrl, payload);
            response.EnsureSuccessStatusCode();

            var ollamaResp = await response.Content
                .ReadFromJsonAsync<OllamaResponse>();

            var reply = ollamaResp?.Response?.Trim() ?? "No response from AI.";
            return new CareerChatResponse(reply);
        }
        catch (Exception ex)
        {
            return new CareerChatResponse($"Error: {ex.Message}");
        }
    }

    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }
}