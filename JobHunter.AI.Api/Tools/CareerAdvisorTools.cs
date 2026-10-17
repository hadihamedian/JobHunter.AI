using System.ComponentModel;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Tools;

public record LeanResume(Guid Id, string Name, int WordCount, bool IsDefault);

public class CareerAdvisorTools(
    ResumeRepository resumeRepository,
    ApplicationRepository applicationRepository)
{
    private const int MaxResumeChars = 4000;

    [Description("Retrieves a lightweight list of all uploaded candidate resumes (ID, Name, WordCount, IsDefault).")]
    public async Task<List<LeanResume>> GetCandidateResumesAsync()
    {
        var all = await resumeRepository.GetAllAsync();
        return all.Select(r => new LeanResume(r.Id, r.Name, r.WordCount, r.IsDefault)).ToList();
    }

    [Description("Fetches the markdown text content of a specific resume by its ID, safely truncated to fit context limits.")]
    public async Task<string> GetResumeContentAsync(
        [Description("The UUID of the resume to retrieve")] Guid resumeId)
    {
        var resume = await resumeRepository.GetByIdAsync(resumeId);
        if (resume is null)
            return "Resume not found.";

        if (resume.Content.Length <= MaxResumeChars)
            return resume.Content;

        return string.Concat(resume.Content.AsSpan(0, MaxResumeChars), "\n[truncated]");
    }

    [Description("Retrieves high-level statistics about the candidate's job search (total applications, active interviews, response rate, rejections).")]
    public async Task<DashboardStats> GetJobSearchStatsAsync()
    {
        return await applicationRepository.GetStatsAsync();
    }

    [Description("Retrieves the candidate's recent job applications including company name, position, status, and source.")]
    public async Task<List<ApplicationSummary>> GetRecentApplicationsAsync()
    {
        var all = await applicationRepository.GetAllAsync();
        return all.Take(20).ToList();
    }
}