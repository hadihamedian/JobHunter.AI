using System.Text.Json;
using Npgsql;
using JobHunter.AI.Api.Models;

namespace JobHunter.AI.Api.Services.Opportunity;

public class OpportunityRepository(NpgsqlDataSource db)
{
    public static async Task InitDbAsync(NpgsqlDataSource dataSource)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS ""Opportunities"" (
    ""Id""              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ""Source""          TEXT NOT NULL,
    ""ExternalId""       TEXT,
    ""SourceUrl""        TEXT,
    ""Title""           TEXT NOT NULL,
    ""Description""     TEXT,
    ""Company""         TEXT,
    ""Client""          TEXT,
    ""Location""        TEXT,
    ""RemoteType""      TEXT,
    ""EngagementType""  TEXT NOT NULL DEFAULT 'OTHER',
    ""BudgetMin""       NUMERIC,
    ""BudgetMax""       NUMERIC,
    ""Currency""        TEXT,
    ""Skills""          JSONB NOT NULL DEFAULT '[]',
    ""PostedAt""        TIMESTAMPTZ,
    ""CollectedAt""     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ""Status""          TEXT NOT NULL DEFAULT 'DISCOVERED',
    ""DedupKey""        TEXT NOT NULL
);

-- DedupKey باید یکتا باشه: جلوگیری از ثبت دوباره‌ی همون فرصت از همون منبع
CREATE UNIQUE INDEX IF NOT EXISTS idx_opportunities_dedupkey
    ON ""Opportunities"" (""DedupKey"");

CREATE INDEX IF NOT EXISTS idx_opportunities_status
    ON ""Opportunities"" (""Status"");

CREATE INDEX IF NOT EXISTS idx_opportunities_source
    ON ""Opportunities"" (""Source"");

CREATE TABLE IF NOT EXISTS ""OpportunityAnalysis"" (
    ""Id""                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ""OpportunityId""               UUID NOT NULL REFERENCES ""Opportunities""(""Id"") ON DELETE CASCADE,
    ""TechnicalMatch""              INTEGER NOT NULL,
    ""ExperienceMatch""             INTEGER NOT NULL,
    ""BudgetScore""                 INTEGER NOT NULL,
    ""ClientQualityScore""          INTEGER NOT NULL DEFAULT -1,
    ""CompetitionScore""            INTEGER NOT NULL DEFAULT -1,
    ""RemoteCompatibilityScore""    INTEGER NOT NULL,
    ""AiRelevanceScore""            INTEGER NOT NULL,
    ""OverallScore""                INTEGER NOT NULL,
    ""Recommendation""              TEXT NOT NULL,
    ""Confidence""                  DOUBLE PRECISION NOT NULL,
    ""Reasons""                     JSONB NOT NULL DEFAULT '[]',
    ""Risks""                       JSONB NOT NULL DEFAULT '[]',
    ""MissingSkills""               JSONB NOT NULL DEFAULT '[]',
    ""SuggestedStrategy""           TEXT,
    ""RemoteCompatibilityFlag""     TEXT NOT NULL DEFAULT 'UNKNOWN',
    ""RemoteCompatibilityReason""   TEXT,
    ""AnalyzedAt""                  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_opportunity_analysis_opportunity
    ON ""OpportunityAnalysis"" (""OpportunityId"");

CREATE INDEX IF NOT EXISTS idx_opportunity_analysis_score
    ON ""OpportunityAnalysis"" (""OverallScore"" DESC);
";
        await using var cmd = dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    // ===== Dedup key: Source + ExternalId اگه موجود باشه، وگرنه Source + نسخه‌ی
    // نرمالایزشده‌ی Title + Company. این منطق باید دقیقاً همینجا مرکزی بمونه
    // تا Normalizer و Repository هیچ‌وقت از هم داستان متفاوتی نگن. =====
    public static string BuildDedupKey(string source, string? externalId, string title, string? company)
    {
        if (!string.IsNullOrWhiteSpace(externalId))
            return $"{source}:{externalId}".ToLowerInvariant();

        var normalizedTitle = new string(title.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray())
            .Trim();
        var normalizedCompany = (company ?? "").ToLowerInvariant().Trim();
        return $"{source}:{normalizedTitle}:{normalizedCompany}";
    }

    // برمی‌گردونه: true اگه insert شد (فرصت جدید بود)، false اگه از قبل بود (duplicate).
    // این متد عمداً throw نمی‌کنه روی conflict — dedup باید silent و بی‌دردسر باشه.
    public async Task<bool> TryInsertAsync(Models.Opportunity opp)
    {
        var sql = @"INSERT INTO ""Opportunities""
(""Id"", ""Source"", ""ExternalId"", ""SourceUrl"", ""Title"", ""Description"", ""Company"", ""Client"",
 ""Location"", ""RemoteType"", ""EngagementType"", ""BudgetMin"", ""BudgetMax"", ""Currency"", ""Skills"",
 ""PostedAt"", ""CollectedAt"", ""Status"", ""DedupKey"")
VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19)
ON CONFLICT (""DedupKey"") DO NOTHING";

        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(opp.Id);
        cmd.Parameters.AddWithValue(opp.Source);
        cmd.Parameters.AddWithValue(opp.ExternalId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.SourceUrl ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.Title);
        cmd.Parameters.AddWithValue(opp.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.Company ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.Client ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.Location ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.RemoteType ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.EngagementType);
        cmd.Parameters.AddWithValue(opp.BudgetMin ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.BudgetMax ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.Currency ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, JsonSerializer.Serialize(opp.Skills));
        cmd.Parameters.AddWithValue(opp.PostedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(opp.CollectedAt);
        cmd.Parameters.AddWithValue(opp.Status);
        cmd.Parameters.AddWithValue(opp.DedupKey);

        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<List<Models.Opportunity>> GetAllAsync(OpportunityFilter? filter = null)
    {
        var list = new List<Models.Opportunity>();
        var sql = @"SELECT ""Id"", ""Source"", ""ExternalId"", ""SourceUrl"", ""Title"", ""Description"", ""Company"",
""Client"", ""Location"", ""RemoteType"", ""EngagementType"", ""BudgetMin"", ""BudgetMax"", ""Currency"",
""Skills"", ""PostedAt"", ""CollectedAt"", ""Status"", ""DedupKey""
FROM ""Opportunities"" WHERE 1=1";

        var paramIndex = 1;
        if (!string.IsNullOrEmpty(filter?.Status)) { sql += $@" AND ""Status"" = ${paramIndex++}"; }
        if (!string.IsNullOrEmpty(filter?.Source)) { sql += $@" AND ""Source"" = ${paramIndex++}"; }
        if (!string.IsNullOrEmpty(filter?.EngagementType)) { sql += $@" AND ""EngagementType"" = ${paramIndex++}"; }
        sql += @" ORDER BY ""CollectedAt"" DESC LIMIT 500";

        await using var cmd = db.CreateCommand(sql);
        if (!string.IsNullOrEmpty(filter?.Status)) cmd.Parameters.AddWithValue(filter.Status);
        if (!string.IsNullOrEmpty(filter?.Source)) cmd.Parameters.AddWithValue(filter.Source);
        if (!string.IsNullOrEmpty(filter?.EngagementType)) cmd.Parameters.AddWithValue(filter.EngagementType);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapRow(reader));
        return list;
    }

    public async Task<Models.Opportunity?> GetByIdAsync(Guid id)
    {
        var sql = @"SELECT ""Id"", ""Source"", ""ExternalId"", ""SourceUrl"", ""Title"", ""Description"", ""Company"",
""Client"", ""Location"", ""RemoteType"", ""EngagementType"", ""BudgetMin"", ""BudgetMax"", ""Currency"",
""Skills"", ""PostedAt"", ""CollectedAt"", ""Status"", ""DedupKey""
FROM ""Opportunities"" WHERE ""Id"" = $1";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapRow(reader) : null;
    }

    public async Task<bool> UpdateStatusAsync(Guid id, string status)
    {
        var sql = @"UPDATE ""Opportunities"" SET ""Status"" = $1 WHERE ""Id"" = $2";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(status);
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task SaveAnalysisAsync(Models.OpportunityAnalysis a)
    {
        var sql = @"INSERT INTO ""OpportunityAnalysis""
(""Id"",""OpportunityId"",""TechnicalMatch"",""ExperienceMatch"",""BudgetScore"",""ClientQualityScore"",
 ""CompetitionScore"",""RemoteCompatibilityScore"",""AiRelevanceScore"",""OverallScore"",""Recommendation"",
 ""Confidence"",""Reasons"",""Risks"",""MissingSkills"",""SuggestedStrategy"",""RemoteCompatibilityFlag"",
 ""RemoteCompatibilityReason"",""AnalyzedAt"")
VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19)";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(a.Id);
        cmd.Parameters.AddWithValue(a.OpportunityId);
        cmd.Parameters.AddWithValue(a.TechnicalMatch);
        cmd.Parameters.AddWithValue(a.ExperienceMatch);
        cmd.Parameters.AddWithValue(a.BudgetScore);
        cmd.Parameters.AddWithValue(a.ClientQualityScore);
        cmd.Parameters.AddWithValue(a.CompetitionScore);
        cmd.Parameters.AddWithValue(a.RemoteCompatibilityScore);
        cmd.Parameters.AddWithValue(a.AiRelevanceScore);
        cmd.Parameters.AddWithValue(a.OverallScore);
        cmd.Parameters.AddWithValue(a.Recommendation);
        cmd.Parameters.AddWithValue(a.Confidence);
        cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, JsonSerializer.Serialize(a.Reasons));
        cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, JsonSerializer.Serialize(a.Risks));
        cmd.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, JsonSerializer.Serialize(a.MissingSkills));
        cmd.Parameters.AddWithValue(a.SuggestedStrategy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(a.RemoteCompatibilityFlag);
        cmd.Parameters.AddWithValue(a.RemoteCompatibilityReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(a.AnalyzedAt);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Models.Opportunity MapRow(NpgsqlDataReader r) => new(
        r.GetGuid(0),
        r.GetString(1),
        r.IsDBNull(2) ? null : r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9),
        r.GetString(10),
        r.IsDBNull(11) ? null : r.GetDecimal(11),
        r.IsDBNull(12) ? null : r.GetDecimal(12),
        r.IsDBNull(13) ? null : r.GetString(13),
        JsonSerializer.Deserialize<List<string>>(r.GetString(14)) ?? new(),
        r.IsDBNull(15) ? null : r.GetDateTime(15),
        r.GetDateTime(16),
        r.GetString(17),
        r.GetString(18)
    );
}