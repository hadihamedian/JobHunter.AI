using System.ComponentModel;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Skills;

public class CareerAdvisorSkills(
    ResumeRepository resumeRepository,
    ApplicationRepository applicationRepository)
{
    [Description("Retrieves the full content of all uploaded resumes to assess skills and experience.")]
    public async Task<List<ResumeDetail>> GetCandidateResumesAsync()
    {
        return await resumeRepository.GetAllWithContentAsync();
    }

    [Description("Retrieves the user's overall job search statistics and application metrics.")]
    public async Task<DashboardStats> GetJobSearchStatsAsync()
    {
        return await applicationRepository.GetStatsAsync();
    }

    [Description("Retrieves the latest job applications with their current statuses and interview stages.")]
    public async Task<List<ApplicationSummary>> GetRecentApplicationsAsync()
    {
        return await applicationRepository.GetAllAsync();
    }
}