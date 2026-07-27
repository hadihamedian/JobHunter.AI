using Npgsql;
using JobHunter.AI.Api.Models;

namespace JobHunter.AI.Api.Services;

public class ResumeRepository(NpgsqlDataSource db)
{
    public static async Task InitDbAsync(NpgsqlDataSource dataSource)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS ""Resumes"" (
    ""Id""          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ""Name""        TEXT NOT NULL,
    ""Content""     TEXT NOT NULL,
    ""WordCount""   INTEGER NOT NULL DEFAULT 0,
    ""IsDefault""   BOOLEAN NOT NULL DEFAULT false,
    ""CreatedAt""   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ""UpdatedAt""   TIMESTAMPTZ
);";
        await using var cmd = dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ResumeSummary>> GetAllAsync()
    {
        var list = new List<ResumeSummary>();
        var sql = @"SELECT ""Id"", ""Name"", ""WordCount"", ""IsDefault"", ""CreatedAt"", ""UpdatedAt""
                    FROM ""Resumes"" ORDER BY ""IsDefault"" DESC, ""CreatedAt"" DESC";
        await using var cmd = db.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ResumeSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetBoolean(3),
                reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5)
            ));
        }
        return list;
    }

    public async Task<ResumeDetail?> GetByIdAsync(Guid id)
    {
        var sql = @"SELECT ""Id"", ""Name"", ""Content"", ""WordCount"", ""IsDefault"", ""CreatedAt"", ""UpdatedAt""
                    FROM ""Resumes"" WHERE ""Id"" = $1";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ResumeDetail(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6)
            );
        }
        return null;
    }

    public async Task<Guid> CreateAsync(CreateResumeRequest req)
    {
        if (req.IsDefault)
        {
            await using var clearCmd = db.CreateCommand(@"UPDATE ""Resumes"" SET ""IsDefault"" = false");
            await clearCmd.ExecuteNonQueryAsync();
        }

        var wordCount = req.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        var sql = @"INSERT INTO ""Resumes"" (""Name"", ""Content"", ""WordCount"", ""IsDefault"")
                    VALUES ($1, $2, $3, $4) RETURNING ""Id""";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(req.Name);
        cmd.Parameters.AddWithValue(req.Content);
        cmd.Parameters.AddWithValue(wordCount);
        cmd.Parameters.AddWithValue(req.IsDefault);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateResumeRequest req)
    {
        if (req.IsDefault == true)
        {
            await using var clearCmd = db.CreateCommand(
                @"UPDATE ""Resumes"" SET ""IsDefault"" = false WHERE ""Id"" != $1");
            clearCmd.Parameters.AddWithValue(id);
            await clearCmd.ExecuteNonQueryAsync();
        }

        var wordCount = req.Content != null
            ? req.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
            : (int?)null;

        var sql = @"UPDATE ""Resumes"" SET
                     ""Name""      = COALESCE($1, ""Name""),
                     ""Content""   = COALESCE($2, ""Content""),
                     ""WordCount"" = COALESCE($3, ""WordCount""),
                     ""IsDefault"" = COALESCE($4, ""IsDefault""),
                     ""UpdatedAt"" = NOW()
                    WHERE ""Id"" = $5";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(req.Name ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Content ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(wordCount ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.IsDefault ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var sql = @"DELETE FROM ""Resumes"" WHERE ""Id"" = $1";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<ResumeDetail>> GetAllWithContentAsync()
    {
        var list = new List<ResumeDetail>();
        var sql = @"SELECT ""Id"", ""Name"", ""Content"", ""WordCount"", ""IsDefault"", ""CreatedAt"", ""UpdatedAt""
                    FROM ""Resumes"" ORDER BY ""IsDefault"" DESC, ""CreatedAt"" DESC";
        await using var cmd = db.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ResumeDetail(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6)
            ));
        }
        return list;
    }
}