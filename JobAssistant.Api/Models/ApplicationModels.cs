namespace JobAssistant.Api.Models;

public record ApplicationSummary(
    Guid Id,
    string CompanyName,
    string Position,
    string? JobUrl,
    string Source,
    string? ResumeVersion,
    string Status,
    int? AtsScore,
    DateTimeOffset? AppliedAt,
    DateTimeOffset CreatedAt,
    int Priority
);

public record ApplicationDetail(
    Guid Id,
    string CompanyName,
    string Position,
    string? JobUrl,
    string? JobDescription,
    string Source,
    string? ResumeVersion,
    string Status,
    int? AtsScore,
    string? Notes,
    DateTimeOffset? AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int Priority
);

public record CreateApplicationRequest(
    string CompanyName,
    string Position,
    string? JobUrl,
    string Source,
    string? ResumeVersion,
    string Status,
    int? AtsScore,
    string? Notes,
    DateTimeOffset? AppliedAt,
    int Priority = 0
);

public record UpdateApplicationRequest(
    string? CompanyName,
    string? Position,
    string? JobUrl,
    string? JobDescription,
    string? Source,
    string? ResumeVersion,
    string? Status,
    int? AtsScore,
    string? Notes,
    DateTimeOffset? AppliedAt,
    int? Priority
);

public record DashboardStats(
    int TotalApplications,
    int ActiveInterviews,
    int ResponseRate,
    int RejectedThisMonth,
    int AppliedThisMonth
);

public record DataChatRequest(
    string Question,
    List<DataChatMessage> History
);

public record DataChatMessage(string Role, string Content);

public record DataChatResponse(
    string TextAnswer,
    DataTable? Table
);

public record DataTable(
    List<string> Columns,
    List<List<string>> Rows
);

public record ReorderRequest(Guid Id, int NewPriority);