using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Services;

public class SemanticSearchService(
    IChatClient chatClient,
    ApplicationRepository repository)
{
    public async Task<SemanticSearchResponse?> SearchAsync(string query)
    {
        var applications = await repository.GetAllLiteAsync();
        if (applications.Count == 0)
            return new SemanticSearchResponse(new List<Guid>(), "No applications yet.");

        var compact = applications.Select(a => new
        {
            id = a.Id,
            company = a.CompanyName,
            position = a.Position,
            source = a.Source,
            status = a.Status,
            atsScore = a.AtsScore,
            appliedAt = a.AppliedAt?.ToString("yyyy-MM-dd"),
            notes = string.IsNullOrWhiteSpace(a.Notes) ? null : (a.Notes.Length > 200 ? a.Notes[..200] : a.Notes)
        });

        var appsJson = JsonSerializer.Serialize(compact, new JsonSerializerOptions { WriteIndented = false });
        var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

        var prompt = $$"""
            You are a semantic search engine for a job application tracker.
            Below is the user's full list of job applications:
            {{appsJson}}

            Today's date is {{today}}.
            User search query: "{{query}}"

            Match applications matching the user's intent. Consider status, dates, notes, and metrics.
            You MUST respond ONLY in valid JSON matching this schema:
            {
              "matchingIds": ["<guid1>", "<guid2>"],
              "explanation": "<short 1-sentence explanation of the match in the user's language>"
            }

            Rules:
            - Only include "id" values present in the input list.
            - If nothing matches, return empty matchingIds.
            """;

        var messages = new List<AiChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var options = new ChatOptions
        {
            Temperature = 0.0f,
            ResponseFormat = ChatResponseFormat.Json
        };

        try
        {
            var response = await chatClient.GetResponseAsync(messages, options);
            var rawText = response.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawText)) return null;

            rawText = CleanJsonFences(rawText);

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var raw = JsonSerializer.Deserialize<RawSemanticResult>(rawText, jsonOptions);
            if (raw is null) return null;

            // Deterministic validation: only accept real IDs from database
            var validIds = applications.Select(a => a.Id).ToHashSet();
            var safeIds = raw.MatchingIds
                .Where(idStr => Guid.TryParse(idStr, out _))
                .Select(Guid.Parse)
                .Where(id => validIds.Contains(id))
                .ToList();

            return new SemanticSearchResponse(safeIds, raw.Explanation);
        }
        catch
        {
            return null;
        }
    }

    private static string CleanJsonFences(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            var lastBacktick = cleaned.LastIndexOf("```");
            if (firstNewline > 0 && lastBacktick > firstNewline)
            {
                cleaned = cleaned.Substring(firstNewline + 1, lastBacktick - firstNewline - 1).Trim();
            }
        }

        var openBrace = cleaned.IndexOf('{');
        var closeBrace = cleaned.LastIndexOf('}');
        if (openBrace >= 0 && closeBrace > openBrace)
        {
            cleaned = cleaned.Substring(openBrace, closeBrace - openBrace + 1);
        }

        return cleaned;
    }

    private class RawSemanticResult
    {
        public List<string> MatchingIds { get; set; } = new();
        public string Explanation { get; set; } = string.Empty;
    }
}