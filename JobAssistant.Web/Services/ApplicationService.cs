using System.Net.Http.Json;
using System.Text.Json;
using JobAssistant.Web.Models;

namespace JobAssistant.Web.Services;

public class ApplicationService(HttpClient http)
{
    public async Task<List<ApplicationSummary>> GetAllAsync(string? status = null)
    {
        var url = string.IsNullOrEmpty(status) ? "/api/applications" : $"/api/applications?status={status}";
        return await http.GetFromJsonAsync<List<ApplicationSummary>>(url) ?? new();
    }

    public async Task<ApplicationDetail?> GetByIdAsync(Guid id) =>
        await http.GetFromJsonAsync<ApplicationDetail>($"/api/applications/{id}");

    public async Task<DashboardStats?> GetStatsAsync() =>
        await http.GetFromJsonAsync<DashboardStats>("/api/applications/stats");

    public async Task<Guid?> CreateAsync(CreateApplicationRequest req)
    {
        var res = await http.PostAsJsonAsync("/api/applications", req);
        if (res.IsSuccessStatusCode)
        {
            var data = await res.Content.ReadFromJsonAsync<JsonElement>();
            return data.GetProperty("id").GetGuid();
        }
        return null;
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateApplicationRequest req)
    {
        var res = await http.PatchAsJsonAsync($"/api/applications/{id}", req);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var res = await http.DeleteAsync($"/api/applications/{id}");
        return res.IsSuccessStatusCode;
    }

    public async Task<DataChatResponse?> ChatAsync(DataChatRequest req)
    {
        var res = await http.PostAsJsonAsync("/api/applications/chat", req);
        if (res.IsSuccessStatusCode) return await res.Content.ReadFromJsonAsync<DataChatResponse>();
        return null;
    }
}