using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Agents;

public class InterviewBankAgent(InterviewBankChatService bankChatService) 
    : IJobHunterAgent<InterviewBankChatRequest, InterviewBankChatResponse>
{
    public string Name => "InterviewBankAgent";
    public string Instructions => "Parses technical and behavioral question query intents and filters the interview question bank deterministically.";

    public async Task<InterviewBankChatResponse> InvokeAsync(InterviewBankChatRequest request, CancellationToken cancellationToken = default)
    {
        return await bankChatService.ChatAsync(request);
    }
}