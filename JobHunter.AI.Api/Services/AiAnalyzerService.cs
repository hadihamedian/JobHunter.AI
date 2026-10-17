using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Tools;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Services;

public class AiAnalyzerService(IChatClient chatClient, AtsEvaluationTools atsTools)
{
    public async Task<AnalyzeResult?> AnalyzeAsync(string resume, string jobDesc)
    {
        // Stage 1: Extract requirements from job description
        var requirements = await ExtractRequirementsAsync(jobDesc);
        if (requirements is null || requirements.Count == 0) return null;

        requirements = requirements
            .GroupBy(r => r.Text.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Stage 2: Deterministic verification via AtsEvaluationTools
        var matchingSkills = new List<string>();
        var missingSkills = new List<string>();
        var experienceRequirements = new List<RequirementItem>();

        foreach (var req in requirements)
        {
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

        // Stage 3: LLM reasoning for experience calculation and final score
        return await FinalizeAsync(resume, experienceRequirements, matchingSkills, missingSkills);
    }

    private async Task<List<RequirementItem>?> ExtractRequirementsAsync(string jobDesc)
    {
        var prompt = $$"""
            Extract job requirements from the text below that can be OBJECTIVELY verified from a resume. For each one, classify it as exactly one of:
            - "skill": a specific technology, tool, framework, language, or technical skill (e.g. "C#", "SQL Server", "ASP.NET", "Design Patterns").
            - "experience": a required MINIMUM number of years of experience (a numeric threshold).

            Do NOT include vague soft-skill, personality, or attitude requirements (e.g. "team spirit", "problem-solving ability"). Exclude these entirely.
            Keep every requirement ATOMIC: one single technology/skill per item. If a sentence lists multiple technologies, split them into separate items.
            Extract EVERY distinct item from any technology list.
            Write each requirement's "text" as the technology/skill name itself (prefer English names for technologies).

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
            Temperature = 0.0f
        };

        var response = await chatClient.GetResponseAsync(messages, options);
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
        List<string> missingSkills)
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

            For each experience requirement: look at ALL job entries in the resume's work history, calculate the duration of each from start/end dates, and SUM them. If the total meets or exceeds the required number, it is met.

            Return ONLY this JSON, no explanation, no markdown fences:
            {
              "atsScore": <number 0-100>,
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
            Temperature = 0.0f
        };

        var response = await chatClient.GetResponseAsync(messages, options);
        var rawText = response.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawText)) return null;

        rawText = CleanJsonFences(rawText);

        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<FinalizeResult>(rawText, jsonOptions);
            if (raw is null) return null;

            var finalMatching = matchingSkills.Concat(raw.ExperienceMet).ToList();
            var finalMissing = missingSkills.Concat(raw.ExperienceNotMet).ToList();

            return new AnalyzeResult(
                raw.AtsScore,
                finalMatching,
                finalMissing,
                raw.CriticalMissingSkills,
                raw.Suggestions
            );
        }
        catch
        {
            return null;
        }
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
        public int AtsScore { get; set; }
        public List<string> ExperienceMet { get; set; } = new();
        public List<string> ExperienceNotMet { get; set; } = new();
        public List<string> CriticalMissingSkills { get; set; } = new();
        public List<string> Suggestions { get; set; } = new();
    }
}