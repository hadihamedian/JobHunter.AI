using System.Net;

namespace JobHunter.AI.Web.Services;

public sealed class RestSessionHandler : DelegatingHandler
{
    public RestSessionHandler() : base(new HttpClientHandler()) { }
    public event Action<string>? Unauthorized;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = request.Headers.Authorization?.Parameter;
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(token))
            Unauthorized?.Invoke(token);
        return response;
    }
}
