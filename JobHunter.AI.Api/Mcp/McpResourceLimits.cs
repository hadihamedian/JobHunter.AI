using System.Threading.RateLimiting;
using ModelContextProtocol;

namespace JobHunter.AI.Api.Mcp;

// Single local deployment: shared budgets across read/write credentials, no queues.
public sealed class McpResourceLimits : IDisposable
{
    private readonly FixedWindowRateLimiter _requests;
    private readonly FixedWindowRateLimiter _analysis;
    private readonly ConcurrencyLimiter _transport;
    private readonly ConcurrencyLimiter _analysisConcurrency;
    public int MaxBodyBytes { get; }

    public McpResourceLimits(IConfiguration configuration)
    {
        var requests = Setting(configuration, "RequestsPerMinute", 120, 1, 600);
        var analysis = Setting(configuration, "AnalysisPerMinute", 12, 1, 120);
        var concurrent = Setting(configuration, "MaxConcurrentRequests", 8, 1, 32);
        MaxBodyBytes = Setting(configuration, "MaxBodyBytes", 65536, 1024, 262144);
        _requests = Window(requests);
        _analysis = Window(analysis);
        _transport = Concurrent(concurrent);
        _analysisConcurrency = Concurrent(1);
    }

    private static int Setting(IConfiguration config, string name, int fallback, int min, int max)
    {
        var value = config.GetValue($"Mcp:Limits:{name}", fallback);
        if (value < min || value > max)
            throw new InvalidOperationException($"Mcp:Limits:{name} must be between {min} and {max}.");
        return value;
    }

    private static FixedWindowRateLimiter Window(int count) => new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = count, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        AutoReplenishment = false, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });

    private static ConcurrencyLimiter Concurrent(int count) => new(new ConcurrencyLimiterOptions
    {
        PermitLimit = count, QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });

    internal RateLimitLease TryRequest()
    {
        _requests.TryReplenish();
        return _requests.AttemptAcquire();
    }

    internal RateLimitLease TryTransport() => _transport.AttemptAcquire();

    internal RateLimitLease AcquireAnalysis()
    {
        var slot = _analysisConcurrency.AttemptAcquire();
        if (!slot.IsAcquired)
        {
            slot.Dispose();
            throw new McpException("Another MCP ATS request is running. Retry later.");
        }
        _analysis.TryReplenish();
        using var rate = _analysis.AttemptAcquire();
        if (!rate.IsAcquired)
        {
            slot.Dispose();
            throw new McpException("MCP ATS rate limit reached. Retry after 60 seconds.");
        }
        return slot;
    }

    public void Dispose()
    {
        _requests.Dispose();
        _analysis.Dispose();
        _transport.Dispose();
        _analysisConcurrency.Dispose();
    }
}
