using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Skills;

namespace JobHunter.AI.Api.Agents;

public class CareerAdvisorAgent : IJobHunterAgent<CareerChatRequest, CareerChatResponse>
{
    private readonly ChatCompletionAgent _agent;

    public string Name => "CareerAdvisorAgent";
    public string Instructions => """
        You are a senior technical career advisor and mentor for software engineers.
        You have direct access to candidate resumes, application history, and job search metrics via tools.
        - Give personalized, direct, and actionable advice.
        - Always inspect data using available tools when answering specific questions about resumes or stats.
        - Keep answers focused (3-5 sentences unless detailed breakdown is needed).
        - Always respond in the exact same language as the user (Persian or English).
        """;

    public CareerAdvisorAgent(Kernel kernel, CareerAdvisorSkills skills)
    {
        var agentKernel = kernel.Clone();
        agentKernel.Plugins.Add(KernelPluginFactory.CreateFromObject(skills, "CareerAdvisorSkills"));

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

    public async Task<CareerChatResponse> InvokeAsync(CareerChatRequest request, CancellationToken cancellationToken = default)
    {
        var history = new ChatHistory();

        foreach (var msg in request.History)
        {
            var role = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? AuthorRole.User
                : AuthorRole.Assistant;
            history.AddMessage(role, msg.Content);
        }

        history.AddUserMessage(request.UserMessage);

        var replyText = string.Empty;
        await foreach (var response in _agent.InvokeAsync(history, cancellationToken: cancellationToken))
        {
            replyText += response.Content;
        }

        return new CareerChatResponse(string.IsNullOrWhiteSpace(replyText) ? "No response generated." : replyText.Trim());
    }
}