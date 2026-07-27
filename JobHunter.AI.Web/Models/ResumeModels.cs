namespace JobHunter.AI.Web.Models;

public record ResumeSummary(
    Guid Id,
    string Name,
    int WordCount,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
);

public record ResumeDetail(
    Guid Id,
    string Name,
    string Content,
    int WordCount,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
);

public record CreateResumeRequest(
    string Name,
    string Content,
    bool IsDefault = false
);

public record UpdateResumeRequest(
    string? Name,
    string? Content,
    bool? IsDefault
);

public record RecommendResumeRequest(string JobDescription);

public record ResumeRecommendation(
    Guid BestResumeId,
    string BestResumeName,
    int BestScore,
    string Reasoning,
    List<ResumeScore> AllScores
);

public record ResumeScore(
    Guid ResumeId,
    string ResumeName,
    int Score
);