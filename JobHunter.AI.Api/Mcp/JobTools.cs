using System.ComponentModel;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;
using ModelContextProtocol.Server;

namespace JobHunter.AI.Api.Mcp;

[McpServerToolType]
public static class JobTools
{
    [McpServerTool(Name = "search_jobs", ReadOnly = true), Description("Search locally saved job records, not external job boards.")]
    public static Task<List<ApplicationSummary>> SearchJobs(ApplicationRepository repository,
        string? query = null, string? status = null, int limit = 20, int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(limit, offset);
        if (query?.Length > 300) throw new ArgumentException("Query is too long.");
        ValidateStatus(status);
        return repository.GetAllAsync(status, query, limit, offset, cancellationToken);
    }

    [McpServerTool(Name = "get_application_history", ReadOnly = true), Description("List stored application records, not a timeline of status changes.")]
    public static Task<List<ApplicationSummary>> GetApplicationHistory(ApplicationRepository repository,
        string? status = null, int limit = 20, int offset = 0,
        CancellationToken cancellationToken = default)
        => SearchJobs(repository, null, status, limit, offset, cancellationToken);

    // One AI-heavy request at a time. No unbounded waiting queue.
    private static readonly SemaphoreSlim AiGate = new(1, 1);

    [McpServerTool(Name = "get_job_analysis", ReadOnly = true), Description("Run the existing ATS analysis for a saved job and resume. Reuses a recent cached analysis when inputs and model settings are unchanged.")]
    public static async Task<AnalyzeResult> GetJobAnalysis(ApplicationRepository repository,
        ResumeRepository resumes, AiAnalyzerService analyzer, Guid applicationId, Guid? resumeId = null,
        CancellationToken cancellationToken = default)
    {
        if (applicationId == Guid.Empty) throw new ArgumentException("Application ID is required.");
        if (!await AiGate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("AI analysis is busy. Retry later.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var job = await repository.GetByIdAsync(applicationId)
                ?? throw new ArgumentException("Application not found.");
            var resume = await ResolveResumeAsync(resumes, resumeId ?? job.ResumeId);
            return await AnalyzeAsync(analyzer, resume, job, timeout.Token);
        }
        finally { AiGate.Release(); }
    }

    [McpServerTool(Name = "get_matching_jobs", ReadOnly = true), Description("Rank a bounded page of Saved jobs by cached or freshly computed ATS score for one resume. This does not search the internet or rank the entire database.")]
    public static async Task<MatchingJobsResult> GetMatchingJobs(ApplicationRepository repository,
        ResumeRepository resumes, AiAnalyzerService analyzer, Guid? resumeId = null,
        int minScore = 60, int candidateLimit = 5, int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (minScore is < 0 or > 100 || candidateLimit is < 1 or > 5 || offset is < 0 or > 100000)
            throw new ArgumentException("Invalid score or page. Candidate limit must be 1–5.");
        if (!await AiGate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("AI analysis is busy. Retry later.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var ct = timeout.Token;
            var resume = await ResolveResumeAsync(resumes, resumeId);
            var candidates = await repository.GetAllAsync("Saved", null, candidateLimit, offset, ct);
            var matches = new List<MatchingJob>();
            var skipped = new List<Guid>();
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var job = await repository.GetByIdAsync(candidate.Id);
                if (job is null || job.Status != "Saved" || string.IsNullOrWhiteSpace(job.JobDescription) || job.JobDescription.Length > 16000)
                { skipped.Add(candidate.Id); continue; }
                var analysis = await analyzer.AnalyzeAsync(resume.Content, job.JobDescription, ct);
                if (analysis is null || analysis.AtsScore is < 0 or > 100) { skipped.Add(candidate.Id); continue; }
                if (analysis.AtsScore >= minScore)
                    matches.Add(new MatchingJob(job.Id, job.CompanyName, job.Position, job.JobUrl, analysis.AtsScore));
            }
            return new MatchingJobsResult(resume.Id, candidates.Count, offset + candidates.Count,
                matches.OrderByDescending(x => x.AtsScore).ThenBy(x => x.ApplicationId).ToList(), skipped);
        }
        finally { AiGate.Release(); }
    }

    [McpServerTool(Name = "update_application_status", ReadOnly = false), Description("Change only the status of one existing application. Requires explicitly enabled write access and the current expected status. Does not submit applications.")]
    public static async Task<StatusUpdateResult> UpdateApplicationStatus(ApplicationRepository repository,
        IConfiguration configuration, Guid applicationId, string expectedStatus, string newStatus,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Mcp:AllowWrites")) throw new InvalidOperationException("MCP write access is disabled.");
        if (applicationId == Guid.Empty || string.IsNullOrWhiteSpace(expectedStatus) || string.IsNullOrWhiteSpace(newStatus))
            throw new ArgumentException("Application ID and both statuses are required.");
        ValidateStatus(expectedStatus);
        ValidateStatus(newStatus);
        if (expectedStatus == newStatus) throw new ArgumentException("The new status must differ from the expected status.");
        var updated = await repository.UpdateStatusAsync(applicationId, expectedStatus, newStatus, cancellationToken);
        return new StatusUpdateResult(updated, updated ? "Updated" : "Not found or current status changed");
    }

    private static async Task<ResumeDetail> ResolveResumeAsync(ResumeRepository resumes, Guid? resumeId)
    {
        if (resumeId is null)
        {
            var defaults = (await resumes.GetAllAsync()).Where(x => x.IsDefault).ToList();
            if (defaults.Count != 1) throw new ArgumentException("Provide a resume ID or configure exactly one default resume.");
            resumeId = defaults[0].Id;
        }
        var resume = await resumes.GetByIdAsync(resumeId.Value) ?? throw new ArgumentException("Resume not found.");
        if (string.IsNullOrWhiteSpace(resume.Content) || resume.Content.Length > 16000)
            throw new ArgumentException("Resume is empty or exceeds the preview input limit.");
        return resume;
    }

    private static async Task<AnalyzeResult> AnalyzeAsync(AiAnalyzerService analyzer, ResumeDetail resume,
        ApplicationDetail job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.JobDescription) || job.JobDescription.Length > 16000)
            throw new ArgumentException("Job description is missing or exceeds the preview input limit.");
        var result = await analyzer.AnalyzeAsync(resume.Content, job.JobDescription, cancellationToken);
        if (result is null || result.AtsScore is < 0 or > 100) throw new InvalidOperationException("ATS analysis failed or returned an invalid score.");
        return result;
    }

    public record MatchingJob(Guid ApplicationId, string CompanyName, string Position, string? JobUrl, int AtsScore);
    public record MatchingJobsResult(Guid ResumeId, int CandidatesExamined, int NextOffset, List<MatchingJob> Matches, List<Guid> SkippedApplicationIds);
    public record StatusUpdateResult(bool Updated, string Message);

    internal static void ValidatePage(int limit, int offset)
    {
        if (limit is < 1 or > 100 || offset is < 0 or > 100000)
            throw new ArgumentException("Limit must be 1–100 and offset 0–100000.");
    }

    internal static void ValidateStatus(string? status)
    {
        if (status is not null and not ("Saved" or "Applied" or "HRInterview" or "TechnicalInterview" or "FinalInterview" or "Offer" or "Rejected"))
            throw new ArgumentException("Unknown application status.");
    }
}
