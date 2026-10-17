namespace JobHunter.AI.Api.Agents;

public interface IJobHunterAgent<TRequest, TResponse>
{
    string Name { get; }
    string Instructions { get; }
    Task<TResponse> InvokeAsync(TRequest request, CancellationToken cancellationToken = default);
}