namespace JobHunter.AI.Api.Models;

public record ChatMessage(string Role, string Content);

public record ChatRequest(
    string ResumeContent,
    string JobDescription,
    string AnalysisSummary,
    List<ChatMessage> History,
    string UserMessage
);

public record ChatResponse(string Reply);

// ===== Career Chat (new) =====
public record CareerChatRequest(
    string UserMessage,
    List<CareerChatMessage> History
);

public record CareerChatMessage(string Role, string Content);

public record CareerChatResponse(string Reply);