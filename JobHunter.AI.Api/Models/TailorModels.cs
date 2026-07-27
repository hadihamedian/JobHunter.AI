namespace JobHunter.AI.Api.Models;

public record TailorRequest(
    Guid ResumeId,
    string JobDescription,
    string Style  // "conservative" یا "aggressive"
);

public record TailorResult(
    string TailoredContent,
    List<string> KeywordsAdded,
    List<string> SectionsRewritten,
    int EstimatedAtsImprovement
);