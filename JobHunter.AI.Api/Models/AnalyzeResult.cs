namespace JobHunter.AI.Api.Models;

public record AnalyzeResult(
    int AtsScore,
    List<string> MatchingSkills,
    List<string> MissingSkills,
    List<string> CriticalMissingSkills,
    List<string> Suggestions
);