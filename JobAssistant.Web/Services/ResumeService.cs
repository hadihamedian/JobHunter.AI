using System.Net.Http.Json;
using JobAssistant.Web.Models;

namespace JobAssistant.Web.Services;

public class ResumeService(HttpClient http)
{
    public async Task<List<ResumeSummary>> GetAllAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<ResumeSummary>>("/api/resumes")
                   ?? new List<ResumeSummary>();
        }
        catch { return new List<ResumeSummary>(); }
    }

    public async Task<ResumeDetail?> GetByIdAsync(Guid id)
    {
        try
        {
            return await http.GetFromJsonAsync<ResumeDetail>($"/api/resumes/{id}");
        }
        catch { return null; }
    }

    public async Task<Guid?> CreateAsync(CreateResumeRequest req)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/resumes", req);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<IdResponse>();
            return result?.Id;
        }
        catch { return null; }
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateResumeRequest req)
    {
        try
        {
            var response = await http.PatchAsJsonAsync($"/api/resumes/{id}", req);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            var response = await http.DeleteAsync($"/api/resumes/{id}");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<ResumeRecommendation?> RecommendAsync(string jobDescription)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/resumes/recommend",
                new RecommendResumeRequest(jobDescription));
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<ResumeRecommendation>();
        }
        catch { return null; }
    }

    private record IdResponse(Guid Id);
}