using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;
using JobHunter.AI.Api.Skills;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Agents;

public class DataChatAgent
{
    private readonly ChatClientAgent _agent;
    private readonly ILogger<DataChatAgent> _logger;

    public const string SystemInstructions = """
        You are a data analyst assistant specialized in job application tracking.
        You have access to tools that fetch live application records and metrics.
        Analyze the data and answer the user question.
        You MUST respond with ONLY a valid JSON object matching this schema:
        {
          "textAnswer": "Your conversational answer here (1-3 sentences)",
          "hasTable": true or false,
          "tableColumns": ["Column1", "Column2"],
          "tableRows": [["val1", "val2"]]
        }
        If no table is needed, set hasTable to false and arrays empty.
        Always answer in the exact same language as the user query (Persian or English).
        """;

    public DataChatAgent(IChatClient chatClient, ApplicationDataSkills skills, ILogger<DataChatAgent> logger)
    {
        _logger = logger;
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(skills.GetDashboardStatsAsync),
            AIFunctionFactory.Create(skills.GetApplicationsAsync),
            AIFunctionFactory.Create(skills.GetApplicationByIdAsync)
        };

        _agent = new ChatClientAgent(chatClient, name: "DataChat", instructions: SystemInstructions, tools: tools);
    }

    public async Task<DataChatResponse?> RunAsync(DataChatRequest request, CancellationToken cancellationToken = default)
    {
        var messages = new List<AiChatMessage>();

        foreach (var historyMsg in request.History)
        {
            var role = historyMsg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new AiChatMessage(role, historyMsg.Content));
        }

        messages.Add(new AiChatMessage(ChatRole.User, request.Question));

        try
        {
            var response = await _agent.RunAsync(messages, session: null, cancellationToken: cancellationToken);
            var rawText = response.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawText))
                return null;

            var cleaned = JsonResponseCleaner.Clean(rawText);

            try
            {
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rawResult = JsonSerializer.Deserialize<RawChatResult>(cleaned, jsonOptions);

                if (rawResult is null)
                    return new DataChatResponse(cleaned, null);

                var table = rawResult.HasTable
                    ? new DataTable(rawResult.TableColumns, rawResult.TableRows)
                    : null;

                return new DataChatResponse(rawResult.TextAnswer, table);
            }
            catch (Exception jsonEx)
            {
                _logger.LogWarning(jsonEx, "DataChatAgent failed to parse structured JSON. Falling back to plain text. Raw output: {Raw}", rawText);
                return new DataChatResponse(cleaned, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing DataChatAgent.");
            return new DataChatResponse($"Error: {ex.Message}", null);
        }
    }

    private class RawChatResult
    {
        public string TextAnswer { get; set; } = string.Empty;
        public bool HasTable { get; set; }
        public List<string> TableColumns { get; set; } = [];
        public List<List<string>> TableRows { get; set; } = [];
    }
}