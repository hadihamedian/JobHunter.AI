using System.Text.Json;
using Microsoft.Extensions.AI;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Skills;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace JobHunter.AI.Api.Services;

public class DataChatService(IChatClient chatClient, ApplicationDataSkills skills)
{
    public async Task<DataChatResponse?> ChatAsync(DataChatRequest request)
    {
        // 1. Convert C# methods into AI-invocable functions (Skills)
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(skills.GetDashboardStatsAsync),
            AIFunctionFactory.Create(skills.GetApplicationsAsync),
            AIFunctionFactory.Create(skills.GetApplicationByIdAsync)
        };

        // 2. Build system instructions using Microsoft.Extensions.AI.ChatMessage
        var messages = new List<AiChatMessage>
        {
            new(ChatRole.System, """
                You are a data assistant specialized in job application analytics.
                You have access to functions for fetching application metrics and details.
                Use the available tools when needed to answer questions accurately.
                Always respond in JSON format matching the following schema:
                {
                  "textAnswer": "Conversational answer (1-3 sentences)",
                  "hasTable": true or false,
                  "tableColumns": ["Column1", "Column2"],
                  "tableRows": [["val1", "val2"]]
                }
                If no table is needed, set hasTable to false and arrays empty.
                Always respond in the same language as the user's question (Persian or English).
                """)
        };

        // 3. Append history
        foreach (var historyMsg in request.History)
        {
            var role = historyMsg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new AiChatMessage(role, historyMsg.Content));
        }

        // 4. Append current user question
        messages.Add(new AiChatMessage(ChatRole.User, request.Question));

        // 5. Invoke chat client with tools enabled
        var chatOptions = new ChatOptions
        {
            Tools = tools,
            Temperature = 0.0f
        };

        var response = await chatClient.GetResponseAsync(messages, chatOptions);
        var rawResponse = response.Message.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawResponse))
            return null;

        // Clean markdown code blocks if the model wrapped JSON output
        if (rawResponse.StartsWith("```"))
        {
            var firstNewline = rawResponse.IndexOf('\n');
            var lastBacktick = rawResponse.LastIndexOf("```");
            if (firstNewline > 0 && lastBacktick > firstNewline)
            {
                rawResponse = rawResponse.Substring(firstNewline + 1, lastBacktick - firstNewline - 1).Trim();
            }
        }

        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<RawChatResult>(rawResponse, jsonOptions);

            if (result is null) return new DataChatResponse(rawResponse, null);

            var table = result.HasTable
                ? new DataTable(result.TableColumns, result.TableRows)
                : null;

            return new DataChatResponse(result.TextAnswer, table);
        }
        catch
        {
            return new DataChatResponse(rawResponse, null);
        }
    }

    private class RawChatResult
    {
        public string TextAnswer { get; set; } = string.Empty;
        public bool HasTable { get; set; }
        public List<string> TableColumns { get; set; } = new();
        public List<List<string>> TableRows { get; set; } = new();
    }
}