using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Agents;

public class AtsAnalyzerAgent(AiAnalyzerService analyzerService) 
    : IJobHunterAgent<AnalyzeRequest, AnalyzeResult?>
{
    public string Name => "AtsAnalyzerAgent";
    public string Instructions => "Deterministically matches job description requirements against resume text with lookaround token matching and evaluates experience thresholds.";

    public async Task<AnalyzeResult?> InvokeAsync(AnalyzeRequest request, CancellationToken cancellationToken = default)
    {
        return await analyzerService.AnalyzeAsync(request.ResumeContent, request.JobDescription);
    }
}