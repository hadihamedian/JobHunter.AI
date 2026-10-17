using System.ComponentModel;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Skills;

public record LeanResume(Guid Id, string Name, int WordCount, bool IsDefault, List<string> SectionHeadings);

public class CareerAdvisorSkills(
    ResumeRepository resumeRepository,
    ApplicationRepository applicationRepository)
{
    private const int MaxResumeContentLength = 4000;

    [Description("Retrieves a lean summary of all uploaded resumes including IDs, names, word counts, and section headings.")]
    public async Task<List<LeanResume>> GetCandidateResumesAsync()
    {
        var resumes = await resumeRepository.GetAllWithContentAsync();
        return resumes.Select(r =>
        {
            var sig = ResumeStructureValidator.Extract(r.Content);
            return new LeanResume(r.Id, r.Name, r.WordCount, r.IsDefault, sig.SectionNames);
        }).ToList();
    }

    [Description("Retrieves the full text of a single resume by its ID, safely truncated if it exceeds maximum capacity.")]
    public async Task<string> GetResumeContentAsync([Description("The GUID of the resume")] Guid id)
    {
        var resume = await resumeRepository.GetByIdAsync(id);
        if (resume is null)
            return "Resume not found.";

        if (resume.Content.Length > MaxResumeContentLength)
            return string.Concat(resume.Content.AsSpan(0, MaxResumeContentLength), "\n\n[truncated]");

        return resume.Content;
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