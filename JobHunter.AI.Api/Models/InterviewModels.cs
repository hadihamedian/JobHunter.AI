namespace JobHunter.AI.Api.Models;

public record InterviewQuestion(
    Guid Id,
    Guid ApplicationId,
    string Question,
    string QuestionType,
    string? Hint,
    string? PersonalNote,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
);

public record GenerateQuestionsRequest(
    Guid ApplicationId,
    string JobDescription,
    string ResumeContent
);

public record UpdateQuestionRequest(
    string? PersonalNote,
    string? Status
);

public record InterviewBankChatRequest(
    string Question,
    List<InterviewBankChatMessage> History
);

public record InterviewBankChatMessage(string Role, string Content);

public record InterviewBankChatResponse(
    string TextAnswer,
    List<InterviewQuestion>? FilteredQuestions
);