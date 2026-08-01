namespace JobHunter.AI.Web.Models;

public record TailorRequest(
    Guid ResumeId,
    string JobDescription,
    string Style
);

public record TailorResult(
    string TailoredContent,
    List<string> KeywordsAdded,
    List<string> SectionsRewritten,
    int EstimatedAtsImprovement,
    bool WasRetried = false,
    double DurationSeconds = 0
);