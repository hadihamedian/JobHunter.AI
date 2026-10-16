using System.ComponentModel;
using System.Text.RegularExpressions;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Skills;

public class AtsEvaluationSkills
{
    [Description("Checks deterministically whether a specific skill or technology exists in the candidate's resume text.")]
    public bool CheckSkillPresence(
        [Description("The full resume text")] string resumeText,
        [Description("The skill or technology keyword to look for")] string skillName)
    {
        if (string.IsNullOrWhiteSpace(resumeText) || string.IsNullOrWhiteSpace(skillName))
            return false;

        var tokens = ExtractCandidateTokens(skillName);
        return tokens.Any(token => token.Length > 1 && resumeText.Contains(token, StringComparison.OrdinalIgnoreCase));
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

    private static List<string> ExtractCandidateTokens(string skillText)
    {
        var parts = skillText
            .Split(new[] { '(', ')', '/', ',', '&' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(p => Regex.Split(p, @"\s+and\s+|\s+و\s+", RegexOptions.IgnoreCase))
            .Select(p => p.Trim())
            .Where(p => p.Length > 1)
            .ToList();

        foreach (Match m in Regex.Matches(skillText, @"[A-Za-z][A-Za-z0-9.#+\-]{1,30}"))
        {
            if (m.Value.Length > 1) parts.Add(m.Value.Trim());
        }

        parts.Add(skillText.Trim());
        return parts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}