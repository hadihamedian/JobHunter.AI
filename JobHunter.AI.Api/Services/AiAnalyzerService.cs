using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Tools;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Services;

public class AiAnalyzerService(IChatClient chatClient, AtsEvaluationTools atsTools, IAtsAnalysisCache? cache = null)
{
    private static readonly SemaphoreSlim AnalysisGate = new(1, 1);

    public async Task<AnalyzeResult?> AnalyzeAsync(string resume, string jobDesc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activeCache = cache is not null && cache.Enabled ? cache : null;
        var key = activeCache?.CreateKey(resume, jobDesc);
        if (activeCache is not null)
        {
            var cached = await activeCache.TryGetAsync(key!, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (AtsAnalysisCache.IsCacheable(cached)) return cached;
        }

        await AnalysisGate.WaitAsync(cancellationToken);
        try
        {
            // Recheck after acquiring the process-wide gate: another request may have filled it.
            if (activeCache is not null)
            {
                var cached = await activeCache.TryGetAsync(key!, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (AtsAnalysisCache.IsCacheable(cached)) return cached;
            }
            var result = await AnalyzeCoreAsync(resume, jobDesc, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (activeCache is not null && AtsAnalysisCache.IsCacheable(result))
                await activeCache.TryStoreAsync(key!, result!, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally { AnalysisGate.Release(); }
    }

    private async Task<AnalyzeResult?> AnalyzeCoreAsync(string resume, string jobDesc, CancellationToken cancellationToken)
    {
        // Stage 1: Extract requirements from job description
        var requirements = await ExtractRequirementsAsync(jobDesc, cancellationToken);
        if (requirements is null || requirements.Count == 0) return null;

        requirements = requirements
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Text)
                && r.Type is "skill" or "experience")
            .Where(r => r.Text.Any(char.IsLetter))
            .Select(r => new RequirementItem { Text = r.Text.Trim(), Type = r.Type })
            .GroupBy(r => r.Text, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (requirements.Count == 0) return null;

        // Stage 2: Deterministic verification via AtsEvaluationTools
        var matchingSkills = new List<string>();
        var missingSkills = new List<string>();
        var experienceRequirements = new List<RequirementItem>();

        foreach (var req in requirements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (req.Type == "experience")
            {
                experienceRequirements.Add(req);
                continue;
            }

            if (atsTools.CheckSkillPresence(resume, req.Text))
                matchingSkills.Add(req.Text);
            else
                missingSkills.Add(req.Text);
        }

        // No experience requirements: avoid a second model call entirely.
        if (experienceRequirements.Count == 0)
            return BuildResult(matchingSkills, missingSkills, [], []);

        // Stage 3: LLM reasoning only for experience classification.
        return await FinalizeAsync(resume, experienceRequirements, matchingSkills, missingSkills, cancellationToken);
    }

    private async Task<List<RequirementItem>?> ExtractRequirementsAsync(string jobDesc, CancellationToken cancellationToken)
    {
        var prompt = $$"""
            Extract job requirements from the text below that can be OBJECTIVELY verified from a resume. For each one, classify it as exactly one of:
            - "skill": a specific technology, tool, framework, language, or technical skill (e.g. "C#", "SQL Server", "ASP.NET", "Design Patterns").
            - "experience": a required MINIMUM number of years of experience (a numeric threshold).

            Do NOT include vague soft-skill, personality, or attitude requirements (e.g. "team spirit", "problem-solving ability"). Exclude these entirely.
            Keep every requirement ATOMIC: one single technology/skill per item. If a sentence lists multiple technologies, split them into separate items.
            Extract EVERY distinct item from any technology list.
            Write skill text as the technology name itself. Never return a bare number as a skill or experience requirement. For experience, preserve the full threshold and relevant domain (e.g. "3 years of microservices experience").

            Job Description:
            {{jobDesc}}

            Return ONLY this JSON, no explanation, no markdown fences:
            {
              "requirements": [
                { "text": "<technology or skill name>", "type": "skill" },
                { "text": "<minimum years requirement>", "type": "experience" }
              ]
            }
            """;

        var messages = new List<AiChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var options = new ChatOptions
        {
            Temperature = 0.0f,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
        var rawText = response.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawText)) return null;

        rawText = CleanJsonFences(rawText);

        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<RequirementsExtraction>(rawText, jsonOptions);
            return raw?.Requirements;
        }
        catch
        {
            return null;
        }
    }

    private async Task<AnalyzeResult?> FinalizeAsync(
        string resume,
        List<RequirementItem> experienceRequirements,
        List<string> matchingSkills,
        List<string> missingSkills, CancellationToken cancellationToken)
    {
        var expList = experienceRequirements.Count > 0
            ? string.Join("\n", experienceRequirements.Select(r => "- " + r.Text))
            : "(none)";

        var confirmedPresent = matchingSkills.Count > 0 ? string.Join(", ", matchingSkills) : "(none)";
        var confirmedAbsent = missingSkills.Count > 0 ? string.Join(", ", missingSkills) : "(none)";

        var prompt = $$"""
            You are finishing an ATS analysis. The skill-matching part is already done by deterministic code — treat it as fact, do not re-evaluate or contradict it.

            Already confirmed as present in the resume:
            {{confirmedPresent}}

            Already confirmed as absent from the resume:
            {{confirmedAbsent}}

            Experience requirements still to evaluate (minimum years thresholds):
            {{expList}}

            Resume (use this ONLY to evaluate the experience requirements above):
            {{resume}}

            For each experience requirement, evaluate only relevant dated work history. Do not double-count overlapping dates. Return the exact requirement text, never job titles. Uncertain evidence belongs in experienceNotMet.

            Return ONLY this JSON, no explanation, no markdown fences:
            {
              "experienceMet": ["<satisfied experience requirement>"],
              "experienceNotMet": ["<unmet experience requirement>"],
              "criticalMissingSkills": ["<subset of absent skills or unmet experience that are mandatory>"],
              "suggestions": ["<2-4 concrete suggestions based on missing items>"]
            }
            """;

        var messages = new List<AiChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var options = new ChatOptions
        {
            Temperature = 0.0f,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
        var rawText = response.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawText)) return null;

        rawText = CleanJsonFences(rawText);

        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<FinalizeResult>(rawText, jsonOptions);
            if (raw is null) return null;

            // Only exact extracted requirements can be classified by the model.
            var met = (raw.ExperienceMet ?? []).Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var notMet = (raw.ExperienceNotMet ?? []).Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var requirement in experienceRequirements)
            {
                if (met.Contains(requirement.Text) && !notMet.Contains(requirement.Text))
                    matchingSkills.Add(requirement.Text);
                else
                    missingSkills.Add(requirement.Text);
            }
            return BuildResult(matchingSkills, missingSkills,
                raw.CriticalMissingSkills ?? [], raw.Suggestions ?? []);
        }
        catch
        {
            return null;
        }
    }

    private static AnalyzeResult BuildResult(List<string> matching, List<string> missing,
        List<string> critical, List<string> suggestions)
    {
        var absent = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var absentSet = absent.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var present = matching.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(v => !absentSet.Contains(v)).ToList();
        var total = present.Count + absent.Count;
        // Equal-weight coverage estimate, not an employer's ATS score.
        var score = total == 0 ? 0 : (int)Math.Floor(100.0 * present.Count / total);
        var criticalMissing = critical.Where(v => v is not null && absentSet.Contains(v.Trim()))
            .Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var advice = suggestions.Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        if (advice.Count == 0)
            advice = absent.Take(4).Select(v => $"Review {v}; add evidence only if you have relevant experience.").ToList();
        return new AnalyzeResult(score, present, absent, criticalMissing, advice);
    }

    private static string CleanJsonFences(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            var lastBacktick = cleaned.LastIndexOf("```");
            if (firstNewline > 0 && lastBacktick > firstNewline)
            {
                cleaned = cleaned.Substring(firstNewline + 1, lastBacktick - firstNewline - 1).Trim();
            }
        }
        return cleaned;
    }

    private class RequirementItem
    {
        public string Text { get; set; } = string.Empty;
        public string Type { get; set; } = "skill";
    }

    private class RequirementsExtraction
    {
        public List<RequirementItem> Requirements { get; set; } = new();
    }

    private class FinalizeResult
    {
        public List<string> ExperienceMet { get; set; } = new();
        public List<string> ExperienceNotMet { get; set; } = new();
        public List<string> CriticalMissingSkills { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
    }
}