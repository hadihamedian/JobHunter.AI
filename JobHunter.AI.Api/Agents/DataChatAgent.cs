using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Skills;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Agents;

public class DataChatAgent : IJobHunterAgent<DataChatRequest, DataChatResponse?>
{
    private readonly ChatCompletionAgent _agent;

    public string Name => "DataChatAgent";
    public string Instructions => """
        You are an analytics assistant specialized in tracking job applications.
        You have access to tools that fetch live metrics and application entries.
        Always output ONLY a valid JSON object matching this schema:
        {
          "textAnswer": "<Conversational summary, 1-3 sentences>",
          "hasTable": true or false,
          "tableColumns": ["Col1", "Col2"],
          "tableRows": [["val1", "val2"]]
        }
        Respond in the same language as the user query (Persian or English).
        """;

    public DataChatAgent(Kernel kernel, ApplicationDataSkills skills)
    {
        var agentKernel = kernel.Clone();
        agentKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(skills, "ApplicationDataSkills"));

        _agent = new ChatCompletionAgent
        {
            Name = Name,
            Instructions = Instructions,
            Kernel = agentKernel,
            Arguments = new KernelArguments(new PromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            })
        };
    }

    public async Task<DataChatResponse?> InvokeAsync(DataChatRequest request, CancellationToken cancellationToken = default)
    {
        var history = new ChatHistory();

        foreach (var msg in request.History)
        {
            var role = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? AuthorRole.User
                : AuthorRole.Assistant;
            history.AddMessage(role, msg.Content);
        }

        history.AddUserMessage(request.Question);

        var rawResponse = string.Empty;
        await foreach (var response in _agent.InvokeAsync(history, cancellationToken: cancellationToken))
        {
            rawResponse += response.Content;
        }

        var cleaned = JsonResponseCleaner.Clean(rawResponse);
        if (string.IsNullOrWhiteSpace(cleaned)) return null;

        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<RawDataResult>(cleaned, jsonOptions);
            if (result is null) return new DataChatResponse(cleaned, null);

            var table = result.HasTable ? new DataTable(result.TableColumns, result.TableRows) : null;
            return new DataChatResponse(result.TextAnswer, table);
        }
        catch
        {
            return new DataChatResponse(cleaned, null);
        }
    }

    private class RawDataResult
    {
        public string TextAnswer { get; set; } = string.Empty;
        public bool HasTable { get; set; }
        public List<string> TableColumns { get; set; } = [];
        public List<List<string>> TableRows { get; set; } = [];
    }
}