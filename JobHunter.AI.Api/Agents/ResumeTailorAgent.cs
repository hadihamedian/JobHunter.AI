using JobHunter.AI.Api.Models;
using JobHunter.AI.Api.Services;

namespace JobHunter.AI.Api.Agents;

public class ResumeTailorAgent(ResumeTailorService tailorService) 
    : IJobHunterAgent<TailorRequest, TailorResult?>
{
    public string Name => "ResumeTailorAgent";
    public string Instructions => "Optimizes resumes for specific job descriptions while verifying structural integrity via deterministic AST-like rules.";

    public async Task<TailorResult?> InvokeAsync(TailorRequest request, CancellationToken cancellationToken = default)
    {
        return await tailorService.TailorAsync(request);
    }
}