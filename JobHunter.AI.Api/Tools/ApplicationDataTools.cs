using System.ComponentModel;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Tools;

public class ApplicationDataTools(ApplicationRepository repository)
{
    [Description("Fetches aggregated statistics for job applications: total count, active interviews, response rate, and monthly counts.")]
    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        return await repository.GetStatsAsync();
    }

    [Description("Fetches the list of job applications, optionally filtered by status (e.g., Applied, Rejected, HRInterview, Offer) or search term.")]
    public async Task<List<ApplicationSummary>> GetApplicationsAsync(
        [Description("Status filter such as Saved, Applied, HRInterview, TechnicalInterview, FinalInterview, Offer, Rejected. Pass empty string for all.")] string status = "",
        [Description("Search keyword for company name, position, or notes. Pass empty string for no keyword filter.")] string search = "")
    {
        var statusFilter = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        var searchFilter = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        return await repository.GetAllAsync(statusFilter, searchFilter);
    }

    [Description("Fetches full details of a specific job application by its unique Guid ID.")]
    public async Task<ApplicationDetail?> GetApplicationByIdAsync(
        [Description("The unique Guid identifier of the application")] Guid id)
    {
        return await repository.GetByIdAsync(id);
    }
}