using System.ComponentModel;
using Microsoft.SemanticKernel;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Skills;

public class ApplicationDataSkills(ApplicationRepository repository)
{
    [KernelFunction("get_dashboard_stats")]
    [Description("Fetches aggregated statistics for job applications: total count, active interviews, response rate, and monthly counts.")]
    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        return await repository.GetStatsAsync();
    }

    [KernelFunction("get_applications")]
    [Description("Fetches the list of job applications, optionally filtered by status (e.g., Applied, Rejected, HRInterview, Offer) or search term.")]
    public async Task<List<ApplicationSummary>> GetApplicationsAsync(
        [Description("Optional status filter such as Saved, Applied, HRInterview, TechnicalInterview, FinalInterview, Offer, Rejected")] string? status = null,
        [Description("Optional search keyword for company name, position, or notes")] string? search = null)
    {
        return await repository.GetAllAsync(status, search);
    }

    [KernelFunction("get_application_by_id")]
    [Description("Fetches full details of a specific job application by its unique Guid ID.")]
    public async Task<ApplicationDetail?> GetApplicationByIdAsync(
        [Description("The unique Guid identifier of the application")] Guid id)
    {
        return await repository.GetByIdAsync(id);
    }
}