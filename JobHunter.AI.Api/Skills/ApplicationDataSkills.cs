using System.ComponentModel;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Skills;

public class ApplicationDataSkills(ApplicationRepository repository)
{
    [Description("Retrieves aggregate statistics about job applications, including total applications, active interviews, response rate, and counts for the current month.")]
    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        return await repository.GetStatsAsync();
    }

    [Description("Searches and filters job applications by status (e.g., Saved, Applied, HRInterview, TechnicalInterview, Offer, Rejected) or by keyword.")]
    public async Task<List<ApplicationSummary>> GetApplicationsAsync(
        [Description("Optional status filter: Saved, Applied, HRInterview, TechnicalInterview, FinalInterview, Offer, Rejected")] string? status = null,
        [Description("Optional search term matching company name, position, or notes")] string? search = null)
    {
        return await repository.GetAllAsync(status, search);
    }

    [Description("Gets detailed information for a specific job application by its unique identifier (GUID).")]
    public async Task<ApplicationDetail?> GetApplicationByIdAsync(
        [Description("The unique identifier of the job application")] Guid id)
    {
        return await repository.GetByIdAsync(id);
    }
}