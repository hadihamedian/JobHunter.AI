using System.Security.Cryptography;
using System.Text.Json;
using JobHunter.AI.Api.Models;
using Npgsql;

namespace JobHunter.AI.Api.Services;

public sealed record AtsCacheSettings(
    bool Enabled, int TtlHours, string Endpoint, string Model, int NumCtx, string ModelRevision);

public interface IAtsAnalysisCache
{
    bool Enabled { get; }
    string CreateKey(string resume, string jobDescription);
    Task<AnalyzeResult?> TryGetAsync(string key, CancellationToken cancellationToken);
    Task TryStoreAsync(string key, AnalyzeResult result, CancellationToken cancellationToken);
}

public sealed class AtsAnalysisCache(
    NpgsqlDataSource db, AtsCacheSettings settings, ILogger<AtsAnalysisCache> logger) : IAtsAnalysisCache
{
    // Bump whenever the ATS prompts, skill rules or scoring algorithm change.
    public const string AlgorithmVersion = "ats-v1";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);
    private bool _initialized;

    public bool Enabled => settings.Enabled && _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled) return;
        if (settings.TtlHours is < 1 or > 168)
        {
            logger.LogWarning("ATS cache disabled: TtlHours must be between 1 and 168.");
            return;
        }

        const string sql = """
            CREATE TABLE IF NOT EXISTS "AtsAnalysisCache" (
                "CacheKey" TEXT PRIMARY KEY,
                "Result" JSONB NOT NULL,
                "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                "ExpiresAt" TIMESTAMPTZ NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_ats_analysis_cache_expiration
                ON "AtsAnalysisCache" ("ExpiresAt");
            """;
        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            await using var cmd = db.CreateCommand(sql);
            await cmd.ExecuteNonQueryAsync(timeout.Token);
            await DeleteExpiredAsync(timeout.Token);
            _initialized = true;
            logger.LogInformation("ATS cache enabled. TTL: {TtlHours} hours.", settings.TtlHours);
        }
        catch (Exception ex) when (IsCacheFailure(ex, cancellationToken))
        {
            logger.LogWarning("ATS cache initialization failed ({ErrorType}); continuing without cache until restart.", ex.GetType().Name);
        }
    }

    public string CreateKey(string resume, string jobDescription) => BuildKey(settings, resume, jobDescription);

    public static string BuildKey(AtsCacheSettings settings, string resume, string jobDescription)
    {
        // JSON framing prevents ambiguous concatenation; hash the exact analyzer inputs.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = AlgorithmVersion,
            settings.Endpoint,
            settings.Model,
            settings.NumCtx,
            settings.ModelRevision,
            resume,
            jobDescription
        });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static bool IsCacheable(AnalyzeResult? result) =>
        result is { AtsScore: >= 0 and <= 100, MatchingSkills: not null,
            MissingSkills: not null, CriticalMissingSkills: not null, Suggestions: not null };

    public async Task<AnalyzeResult?> TryGetAsync(string key, CancellationToken cancellationToken)
    {
        if (!Enabled) return null;
        const string sql = """
            SELECT "Result"::text FROM "AtsAnalysisCache"
            WHERE "CacheKey" = $1 AND "ExpiresAt" > NOW();
            """;
        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue(key);
            var json = await cmd.ExecuteScalarAsync(timeout.Token) as string;
            var result = json is null ? null : JsonSerializer.Deserialize<AnalyzeResult>(json);
            if (IsCacheable(result))
            {
                logger.LogInformation("ATS cache HIT");
                return result;
            }
            logger.LogInformation("ATS cache MISS");
            return null;
        }
        catch (Exception ex) when (IsCacheFailure(ex, cancellationToken) || ex is JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogWarning("ATS cache read failed ({ErrorType}); performing fresh analysis.", ex.GetType().Name);
            return null;
        }
    }

    public async Task TryStoreAsync(string key, AnalyzeResult result, CancellationToken cancellationToken)
    {
        if (!Enabled || !IsCacheable(result)) return;
        const string sql = """
            INSERT INTO "AtsAnalysisCache" ("CacheKey", "Result", "ExpiresAt")
            VALUES ($1, $2::jsonb, NOW() + $3)
            ON CONFLICT ("CacheKey") DO UPDATE SET
                "Result" = EXCLUDED."Result",
                "CreatedAt" = NOW(),
                "ExpiresAt" = EXCLUDED."ExpiresAt";
            """;
        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue(key);
            cmd.Parameters.AddWithValue(JsonSerializer.Serialize(result));
            cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Interval, TimeSpan.FromHours(settings.TtlHours));
            await cmd.ExecuteNonQueryAsync(timeout.Token);
            logger.LogInformation("ATS cache STORED");
            await DeleteExpiredAsync(timeout.Token);
        }
        catch (Exception ex) when (IsCacheFailure(ex, cancellationToken))
        {
            logger.LogWarning("ATS cache write/cleanup failed ({ErrorType}); returning the fresh analysis.", ex.GetType().Name);
        }
    }

    private async Task DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        await using var cmd = db.CreateCommand("""
            DELETE FROM "AtsAnalysisCache" WHERE "ExpiresAt" <= NOW();
            """);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(OperationTimeout);
        return cts;
    }

    // Cache errors are optional; caller cancellation must still propagate.
    private static bool IsCacheFailure(Exception ex, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        ex is NpgsqlException or TimeoutException or OperationCanceledException;
}
