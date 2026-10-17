using System.ComponentModel;
using System.Text.RegularExpressions;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Skills;

public class AtsEvaluationSkills
{
    private static readonly Dictionary<string, string[]> ConservativeSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C#"] = ["C#", "csharp", "سی‌شارپ", "سی شارپ"],
        [".NET"] = [".NET", "dotnet", "دات‌نت", "دات نت"],
        ["PostgreSQL"] = ["PostgreSQL", "Postgres"],
        ["JavaScript"] = ["JavaScript", "JS"],
        ["TypeScript"] = ["TypeScript", "TS"],
        ["Entity Framework"] = ["Entity Framework", "EF Core", "EFCore"]
    };

    [Description("Deterministically checks whether a specific technical skill or its conservative alias is present in the resume text.")]
    public bool CheckSkillPresence(
        [Description("The full resume text")] string resume,
        [Description("The skill or technology requirement to check")] string skillText)
    {
        if (string.IsNullOrWhiteSpace(resume) || string.IsNullOrWhiteSpace(skillText))
            return false;

        var candidates = ExtractCandidateTokens(skillText);

        foreach (var token in candidates)
        {
            if (MatchesTokenOrSynonyms(resume, token))
                return true;
        }

        return false;
    }

    [Description("Validates structural integrity between the original markdown resume and the tailored markdown resume.")]
    public List<string> ValidateResumeStructure(
        [Description("The original resume markdown")] string originalMarkdown,
        [Description("The tailored resume markdown")] string tailoredMarkdown)
    {
        var origSig = ResumeStructureValidator.Extract(originalMarkdown);
        var tailoredSig = ResumeStructureValidator.Extract(tailoredMarkdown);

        var violations = ResumeStructureValidator.Compare(origSig, tailoredSig);
        violations.AddRange(ResumeStructureValidator.DetectDuplicateHeadings(tailoredMarkdown));

        return violations;
    }

    private static bool MatchesTokenOrSynonyms(string resume, string token)
    {
        if (ContainsWholeToken(resume, token))
            return true;

        foreach (var (canonical, aliases) in ConservativeSynonyms)
        {
            if (string.Equals(canonical, token, StringComparison.OrdinalIgnoreCase) ||
                aliases.Any(a => string.Equals(a, token, StringComparison.OrdinalIgnoreCase)))
            {
                if (aliases.Any(alias => ContainsWholeToken(resume, alias)))
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsWholeToken(string text, string token)
    {
        var escaped = Regex.Escape(token.Trim());
        var pattern = $@"(?<![A-Za-z0-9]){escaped}(?![A-Za-z0-9])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static List<string> ExtractCandidateTokens(string skillText)
    {
        var parts = skillText
            .Split(['(', ')', '/', ',', '&'], StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => Regex.Split(p, @"\s+and\s+|\s+or\s+|\s+و\s+", RegexOptions.IgnoreCase))
            .Select(p => p.Trim())
            .Where(p => p.Length > 1)
            .ToList();

        parts.Add(skillText.Trim());
        return parts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}