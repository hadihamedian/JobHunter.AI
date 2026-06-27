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
    DateTimeOffset CreatedAt
);

public record ApplicationDetail(
    Guid Id,
    string CompanyName,
    string Position,
    string? JobUrl,
    string Source,
    string? ResumeVersion,
    string Status,
    int? AtsScore,
    string? Notes,
    DateTimeOffset? AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
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
    DateTimeOffset? AppliedAt
);

public record UpdateApplicationRequest(
    string? CompanyName,
    string? Position,
    string? JobUrl,
    string? Source,
    string? ResumeVersion,
    string? Status,
    int? AtsScore,
    string? Notes,
    DateTimeOffset? AppliedAt
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