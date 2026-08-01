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
    int EstimatedAtsImprovement,
    bool WasRetried = false,          // جدید
    double DurationSeconds = 0        // جدید — کل زمان تولید (شامل retry اگه بود)
);