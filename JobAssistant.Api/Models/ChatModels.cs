namespace JobAssistant.Api.Models;

public record ChatMessage(string Role, string Content);

public record ChatRequest(
    string ResumeContent,
    string JobDescription,
    string AnalysisSummary,
    List<ChatMessage> History,
    string UserMessage
);

public record ChatResponse(string Reply);