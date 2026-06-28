using Npgsql;
using JobAssistant.Api.Models;

namespace JobAssistant.Api.Services;

public class InterviewRepository(NpgsqlDataSource db)
{
    public static async Task InitDbAsync(NpgsqlDataSource dataSource)
    {
        var sql = @"
CREATE TABLE IF NOT EXISTS ""InterviewQuestions"" (
    ""Id""              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ""ApplicationId""   UUID NOT NULL,
    ""Question""        TEXT NOT NULL,
    ""QuestionType""    TEXT NOT NULL DEFAULT 'Technical',
    ""Hint""            TEXT,
    ""PersonalNote""    TEXT,
    ""Status""          TEXT NOT NULL DEFAULT 'NotPracticed',
    ""CreatedAt""       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ""UpdatedAt""       TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_interview_questions_app 
    ON ""InterviewQuestions"" (""ApplicationId"");
";
        await using var cmd = dataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<InterviewQuestion>> GetByApplicationAsync(Guid applicationId)
    {
        var list = new List<InterviewQuestion>();
        var sql = @"SELECT ""Id"", ""ApplicationId"", ""Question"", ""QuestionType"", 
                           ""Hint"", ""PersonalNote"", ""Status"", ""CreatedAt"", ""UpdatedAt""
                    FROM ""InterviewQuestions""
                    WHERE ""ApplicationId"" = $1
                    ORDER BY ""QuestionType"" ASC, ""CreatedAt"" ASC";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(applicationId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapRow(reader));
        return list;
    }

    public async Task<List<InterviewQuestion>> GetAllAsync()
    {
        var list = new List<InterviewQuestion>();
        var sql = @"SELECT ""Id"", ""ApplicationId"", ""Question"", ""QuestionType"", 
                           ""Hint"", ""PersonalNote"", ""Status"", ""CreatedAt"", ""UpdatedAt""
                    FROM ""InterviewQuestions""
                    ORDER BY ""QuestionType"" ASC, ""CreatedAt"" DESC";
        await using var cmd = db.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(MapRow(reader));
        return list;
    }

    public async Task<List<Guid>> SaveQuestionsAsync(
        Guid applicationId,
        List<(string Question, string Type, string Hint)> questions)
    {
        var ids = new List<Guid>();
        foreach (var q in questions)
        {
            var sql = @"INSERT INTO ""InterviewQuestions"" 
                        (""ApplicationId"", ""Question"", ""QuestionType"", ""Hint"")
                        VALUES ($1, $2, $3, $4) RETURNING ""Id""";
            await using var cmd = db.CreateCommand(sql);
            cmd.Parameters.AddWithValue(applicationId);
            cmd.Parameters.AddWithValue(q.Question);
            cmd.Parameters.AddWithValue(q.Type);
            cmd.Parameters.AddWithValue(q.Hint);
            var id = (Guid)(await cmd.ExecuteScalarAsync())!;
            ids.Add(id);
        }
        return ids;
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateQuestionRequest req)
    {
        var sql = @"UPDATE ""InterviewQuestions"" SET
                     ""PersonalNote"" = COALESCE($1, ""PersonalNote""),
                     ""Status""       = COALESCE($2, ""Status""),
                     ""UpdatedAt""    = NOW()
                    WHERE ""Id"" = $3";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(req.PersonalNote ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(req.Status ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteByApplicationAsync(Guid applicationId)
    {
        var sql = @"DELETE FROM ""InterviewQuestions"" WHERE ""ApplicationId"" = $1";
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue(applicationId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static InterviewQuestion MapRow(NpgsqlDataReader r) => new(
        r.GetGuid(0),
        r.GetGuid(1),
        r.GetString(2),
        r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.GetString(6),
        r.GetDateTime(7),
        r.IsDBNull(8) ? null : r.GetDateTime(8)
    );
}