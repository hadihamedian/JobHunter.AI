namespace JobHunter.AI.Api.Models;

public record AnalyzeRequest(string ResumeContent, string JobDescription);

public record AnalyzeByIdRequest(Guid ResumeId, string JobDescription);