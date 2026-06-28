using System.Net.Http.Json;
using JobAssistant.Web.Models;

namespace JobAssistant.Web.Services;

public class InterviewService(HttpClient http)
{
    public async Task<List<InterviewQuestion>> GetByApplicationAsync(Guid applicationId)
    {
        try
        {
            return await http.GetFromJsonAsync<List<InterviewQuestion>>(
                $"/api/interviews/application/{applicationId}") ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<InterviewQuestion>> GetAllAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<InterviewQuestion>>(
                "/api/interviews") ?? new();
        }
        catch { return new(); }
    }

    public async Task<List<InterviewQuestion>> GenerateAsync(
        GenerateQuestionsRequest req)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/interviews/generate", req);
            if (!response.IsSuccessStatusCode) return new();
            return await response.Content
                .ReadFromJsonAsync<List<InterviewQuestion>>() ?? new();
        }
        catch { return new(); }
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateQuestionRequest req)
    {
        try
        {
            var response = await http.PatchAsJsonAsync(
                $"/api/interviews/{id}", req);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteByApplicationAsync(Guid applicationId)
    {
        try
        {
            var response = await http.DeleteAsync(
                $"/api/interviews/application/{applicationId}");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<InterviewBankChatResponse?> ChatAsync(
        InterviewBankChatRequest req)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/interviews/chat", req);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content
                .ReadFromJsonAsync<InterviewBankChatResponse>();
        }
        catch { return null; }
    }
}