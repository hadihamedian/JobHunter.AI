namespace JobHunter.AI.Web.Models;

public record CareerChatRequest(
    string UserMessage,
    List<CareerChatMessage> History
);

public record CareerChatMessage(string Role, string Content);

public record CareerChatResponse(string Reply);