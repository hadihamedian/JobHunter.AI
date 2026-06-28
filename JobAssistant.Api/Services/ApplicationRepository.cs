using Npgsql;
using JobAssistant.Api.Models;

namespace JobAssistant.Api.Services;

public class ApplicationRepository(NpgsqlDataSource db)
{
    public static async Task InitDbAsync(NpgsqlDataSource dataSource)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS ""Applications"" (
    ""Id""           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ""CompanyName""  TEXT NOT NULL,
    ""Position""     TEXT NOT NULL,
    ""JobUrl""       TEXT,
    ""Source""       TEXT NOT NULL DEFAULT 'Direct',
    ""ResumeVersion"" TEXT,
    ""Status""       TEXT NOT NULL DEFAULT 'Saved',
    ""AtsScore""     INTEGER,
    ""Notes""        TEXT,
    ""AppliedAt""    TIMESTAMPTZ,
    ""CreatedAt""    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ""UpdatedAt""    TIMESTAMPTZ
);
ALTER TABLE ""Applications"" ADD COLUMN IF NOT EXISTS ""Priority"" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ""Applications"" ADD COLUMN IF NOT EXISTS ""JobDescription"" TEXT;
";
        await using var cmd = dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ApplicationSummary>> GetAllAsync(string? status = null)
    {
        var list = new List<ApplicationSummary>();
        var sql = @"SELECT ""Id"", ""CompanyName"", ""Position"", ""JobUrl"", ""Source"", ""ResumeVersion"", ""Status"", ""AtsScore"", ""AppliedAt"", ""CreatedAt"", ""Priority""
FROM ""Applications""";
        if (!string.IsNullOrEmpty(status)) sql += @" WHERE ""Status"" = $1";
        sql += @" ORDER BY ""Priority"" ASC, ""CreatedAt"" DESC";
        await using var cmd = db.CreateCommand(sql);
        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue(status);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ApplicationSummary(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                reader.GetDateTime(9),
                reader.GetInt32(10)
            ));
        }
        return list;
    }

    public async Task<ApplicationDetail?> GetByIdAsync(Guid id)
    {
        var sql = @"SELECT ""Id"", ""CompanyName"", ""Position"", ""JobUrl"", ""JobDescription"", ""Source"", ""ResumeVersion"", ""Status"", ""AtsScore"", ""Notes"", ""AppliedAt"", ""CreatedAt"", ""UpdatedAt"", ""Priority""
FROM ""Applications"" WHERE ""Id"" = $1";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ApplicationDetail(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                reader.GetDateTime(11),
                reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                reader.GetInt32(13)
            );
        }
        return null;
    }

    public async Task<Guid> CreateAsync(CreateApplicationRequest req)
    {
        var sql = @"INSERT INTO ""Applications"" (""CompanyName"", ""Position"", ""JobUrl"", ""Source"", ""ResumeVersion"", ""Status"", ""AtsScore"", ""Notes"", ""AppliedAt"", ""Priority"")
VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10) RETURNING ""Id"";";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(req.CompanyName);
        cmd.Parameters.AddWithValue(req.Position);
        cmd.Parameters.AddWithValue(req.JobUrl ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Source);
        cmd.Parameters.AddWithValue(req.ResumeVersion ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Status);
        cmd.Parameters.AddWithValue(req.AtsScore ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.AppliedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Priority);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateApplicationRequest req)
    {
        var sql = @"UPDATE ""Applications"" SET
""CompanyName"" = COALESCE($1, ""CompanyName""),
""Position"" = COALESCE($2, ""Position""),
""JobUrl"" = $3,
""JobDescription"" = $4,
""Source"" = COALESCE($5, ""Source""),
""ResumeVersion"" = $6,
""Status"" = COALESCE($7, ""Status""),
""AtsScore"" = $8,
""Notes"" = $9,
""AppliedAt"" = $10,
""Priority"" = COALESCE($12, ""Priority""),
""UpdatedAt"" = NOW()
WHERE ""Id"" = $11";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(req.CompanyName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Position ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.JobUrl ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.JobDescription ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Source ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.ResumeVersion ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Status ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.AtsScore ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.AppliedAt ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(req.Priority ?? (object)DBNull.Value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var sql = @"DELETE FROM ""Applications"" WHERE ""Id"" = $1";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<DashboardStats> GetStatsAsync()
    {
        var sql = @"
SELECT
    (SELECT COUNT(*) FROM ""Applications"") as Total,
    (SELECT COUNT(*) FROM ""Applications"" WHERE ""Status"" IN ('HRInterview','TechnicalInterview','FinalInterview')) as ActiveInt,
    (SELECT ROUND(COUNT(*) FILTER(WHERE ""Status"" != 'Applied' AND ""Status"" != 'Saved') * 100.0 / NULLIF(COUNT(*),0)) FROM ""Applications"") as RespRate,
    (SELECT COUNT(*) FROM ""Applications"" WHERE ""Status""='Rejected' AND ""CreatedAt"" >= date_trunc('month', NOW())) as RejMonth,
    (SELECT COUNT(*) FROM ""Applications"" WHERE ""CreatedAt"" >= date_trunc('month', NOW())) as AppMonth
";
        await using var cmd = db.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new DashboardStats(
                reader.IsDBNull(0) ? 0 : (int)reader.GetInt64(0),
                reader.IsDBNull(1) ? 0 : (int)reader.GetInt64(1),
                reader.IsDBNull(2) ? 0 : (int)reader.GetDecimal(2),
                reader.IsDBNull(3) ? 0 : (int)reader.GetInt64(3),
                reader.IsDBNull(4) ? 0 : (int)reader.GetInt64(4)
            );
        }
        return new DashboardStats(0, 0, 0, 0, 0);
    }

    public async Task<List<ApplicationDetail>> GetAllForChatAsync()
    {
        var list = new List<ApplicationDetail>();
        var sql = @"SELECT ""Id"", ""CompanyName"", ""Position"", ""JobUrl"", ""JobDescription"", ""Source"", ""ResumeVersion"", ""Status"", ""AtsScore"", ""Notes"", ""AppliedAt"", ""CreatedAt"", ""UpdatedAt"", ""Priority""
FROM ""Applications"" ORDER BY ""CreatedAt"" DESC LIMIT 20";
        await using var cmd = db.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ApplicationDetail(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                reader.GetDateTime(11),
                reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                reader.GetInt32(13)
            ));
        }
        return list;
    }

    public async Task<bool> MoveUpAsync(Guid id)
    {
        await using var getCmd = db.CreateCommand(@"SELECT ""Priority"" FROM ""Applications"" WHERE ""Id"" = $1");
        getCmd.Parameters.AddWithValue(id);
        var current = await getCmd.ExecuteScalarAsync();
        if (current is null) return false;

        int currentPriority = Convert.ToInt32(current);
        if (currentPriority == 0) return false;

        int newPriority = currentPriority - 1;

        await using var swapCmd = db.CreateCommand(@"
            UPDATE ""Applications"" 
            SET ""Priority"" = $1 
            WHERE ""Priority"" = $2 AND ""Id"" != $3");
        swapCmd.Parameters.AddWithValue(currentPriority);
        swapCmd.Parameters.AddWithValue(newPriority);
        swapCmd.Parameters.AddWithValue(id);
        await swapCmd.ExecuteNonQueryAsync();

        await using var updateCmd = db.CreateCommand(@"
            UPDATE ""Applications"" SET ""Priority"" = $1 WHERE ""Id"" = $2");
        updateCmd.Parameters.AddWithValue(newPriority);
        updateCmd.Parameters.AddWithValue(id);
        return await updateCmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> MoveDownAsync(Guid id)
    {
        await using var getCmd = db.CreateCommand(@"SELECT ""Priority"" FROM ""Applications"" WHERE ""Id"" = $1");
        getCmd.Parameters.AddWithValue(id);
        var current = await getCmd.ExecuteScalarAsync();
        if (current is null) return false;

        int currentPriority = Convert.ToInt32(current);
        int newPriority = currentPriority + 1;

        await using var swapCmd = db.CreateCommand(@"
            UPDATE ""Applications"" 
            SET ""Priority"" = $1 
            WHERE ""Priority"" = $2 AND ""Id"" != $3");
        swapCmd.Parameters.AddWithValue(currentPriority);
        swapCmd.Parameters.AddWithValue(newPriority);
        swapCmd.Parameters.AddWithValue(id);
        await swapCmd.ExecuteNonQueryAsync();

        await using var updateCmd = db.CreateCommand(@"
            UPDATE ""Applications"" SET ""Priority"" = $1 WHERE ""Id"" = $2");
        updateCmd.Parameters.AddWithValue(newPriority);
        updateCmd.Parameters.AddWithValue(id);
        return await updateCmd.ExecuteNonQueryAsync() > 0;
    }
}