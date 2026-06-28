using System.Net.Http.Json;
using System.Text.Json;
using JobAssistant.Web.Models;

namespace JobAssistant.Web.Services;

public class ApplicationService(HttpClient http)
{
    public async Task<List<ApplicationSummary>> GetAllAsync(string? status = null, string? search = null)
    {
        var url = "/api/applications?";
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(status)) parts.Add($"status={status}");
        if (!string.IsNullOrEmpty(search)) parts.Add($"q={Uri.EscapeDataString(search)}");
        url += string.Join("&", parts);
        if (url.EndsWith("?")) url = url.TrimEnd('?');
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

    public async Task<bool> MoveUpAsync(Guid id)
    {
        try
        {
            var response = await http.PostAsync($"/api/applications/{id}/move-up", null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> MoveDownAsync(Guid id)
    {
        try
        {
            var response = await http.PostAsync($"/api/applications/{id}/move-down", null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}