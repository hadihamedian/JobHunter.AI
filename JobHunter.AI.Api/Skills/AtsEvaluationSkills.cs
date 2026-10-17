using System.ComponentModel;
using System.Text.RegularExpressions;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Skills;

public class AtsEvaluationSkills
{
    private static readonly Dictionary<string, string[]> ConservativeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C#"] = ["csharp", "c#"],
        ["csharp"] = ["c#", "csharp"],
        [".NET"] = ["dotnet", ".net", ".net core"],
        ["dotnet"] = [".net", "dotnet", ".net core"],
        [".NET Core"] = [".net", "dotnet", ".net core"],
        ["PostgreSQL"] = ["postgres", "postgresql"],
        ["Postgres"] = ["postgresql", "postgres"],
        ["JavaScript"] = ["js", "javascript"],
        ["JS"] = ["javascript", "js"],
        ["Entity Framework"] = ["ef core", "ef", "entity framework"],
        ["EF Core"] = ["entity framework", "ef", "ef core"],
        ["EF"] = ["entity framework", "ef core", "ef"]
    };

    [Description("Checks deterministically whether a specific skill or technology exists in the candidate's resume text.")]
    public bool CheckSkillPresence(
        [Description("The full resume text")] string resumeText,
        [Description("The skill or technology keyword to look for")] string skillName)
    {
        if (string.IsNullOrWhiteSpace(resumeText) || string.IsNullOrWhiteSpace(skillName))
            return false;

        var normalizedResume = NormalizeText(resumeText);
        var candidates = ExtractCandidateTokens(skillName);

        foreach (var candidate in candidates)
        {
            var variants = GetSearchVariants(candidate);
            foreach (var variant in variants)
            {
                var pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(variant)}(?![A-Za-z0-9])";
                if (Regex.IsMatch(normalizedResume, pattern, RegexOptions.IgnoreCase))
                    return true;
            }
        }

        return false;
    }

    [Description("Validates that a tailored resume maintains full structural parity (all sections, bullet counts, and project items) with the original resume.")]
    public List<string> ValidateResumeStructure(
        [Description("The original resume markdown content")] string originalResume,
        [Description("The newly tailored resume markdown content")] string tailoredResume)
    {
        var originalSig = ResumeStructureValidator.Extract(originalResume);
        var tailoredSig = ResumeStructureValidator.Extract(tailoredResume);

        var violations = ResumeStructureValidator.Compare(originalSig, tailoredSig);
        violations.AddRange(ResumeStructureValidator.DetectDuplicateHeadings(tailoredResume));

        return violations;
    }

    public static List<string> ExtractCandidateTokens(string skillText)
    {
        if (string.IsNullOrWhiteSpace(skillText))
            return [];

        var results = new List<string>();
        var cleanInput = skillText.Trim();

        var parenMatches = Regex.Matches(cleanInput, @"\(([^)]+)\)");
        foreach (Match m in parenMatches)
        {
            var inside = m.Groups[1].Value.Trim();
            if (inside.Length > 0)
                results.Add(inside);
        }

        var withoutParens = Regex.Replace(cleanInput, @"\([^)]*\)", " ").Trim();
        var rawAlternatives = Regex.Split(withoutParens, @"\s*[,/&]\s*|\s+(?:and|و)\s+", RegexOptions.IgnoreCase);

        foreach (var alt in rawAlternatives)
        {
            var trimmed = NormalizeText(alt);
            if (trimmed.Length > 0)
                results.Add(trimmed);
        }

        if (results.Count == 0 && cleanInput.Length > 0)
            results.Add(NormalizeText(cleanInput));

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> GetSearchVariants(string token)
    {
        var normalized = NormalizeText(token);
        var list = new List<string> { normalized };

        if (ConservativeAliases.TryGetValue(normalized, out var aliases))
        {
            foreach (var alias in aliases)
                list.Add(NormalizeText(alias));
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return Regex.Replace(text.Trim(), @"\s+", " ");
    }
}