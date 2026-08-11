using System.Net.Http.Json;
using JobHunter.AI.Web.Models;

namespace JobHunter.AI.Web.Services;

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

    public async Task<ResumeUsage?> GetUsageAsync(Guid id)
    {
        try
        {
            return await http.GetFromJsonAsync<ResumeUsage>($"/api/resumes/{id}/usage");
        }
        catch { return null; }
    }

    public async Task<ResumeDeleteResult> DeleteAsync(Guid id)
    {
        try
        {
            var response = await http.DeleteAsync($"/api/resumes/{id}");
            if (response.IsSuccessStatusCode)
                return new ResumeDeleteResult(true, null);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var usage = await response.Content.ReadFromJsonAsync<ResumeUsage>();
                return new ResumeDeleteResult(false, usage?.Applications ?? new());
            }

            return new ResumeDeleteResult(false, new());
        }
        catch { return new ResumeDeleteResult(false, new()); }
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