using System.Text;
using Npgsql;

namespace JobHunter.AI.Api.Services;

public static class LocalDatabaseSetup
{
    private static NpgsqlConnectionStringBuilder Validate(string connectionString, IWebHostEnvironment environment, bool seed)
    {
        var cs = new NpgsqlConnectionStringBuilder(connectionString);
        if (!environment.IsDevelopment() || cs.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException("Automatic database creation and demo seeding require a local Development database.");
        if (string.IsNullOrWhiteSpace(cs.Database) || Encoding.UTF8.GetByteCount(cs.Database) > 63 ||
            cs.Database.Contains('\0') || cs.Database is "postgres" or "template0" or "template1")
            throw new InvalidOperationException("Configure a valid application database name.");
        if (seed && !cs.Database.EndsWith("_demo", StringComparison.Ordinal))
            throw new InvalidOperationException("Demo seeding requires a database name ending in _demo.");
        return cs;
    }

    public static async Task EnsureDatabaseAsync(string connectionString, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var create = configuration.GetValue("Database:AutoCreate", false);
        var seed = configuration.GetValue("Demo:SeedData", false);
        if (!create && !seed) return;
        var cs = Validate(connectionString, environment, seed);
        if (!create) return;
        // First try the requested database. Existing databases require no maintenance permission.
        try
        {
            await using var target = new NpgsqlConnection(cs.ConnectionString);
            await target.OpenAsync();
            return;
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000") { }
        var name = cs.Database!;
        cs.Database = "postgres";
        await using var admin = new NpgsqlConnection(cs.ConnectionString);
        await admin.OpenAsync();
        using var quoting = new NpgsqlCommandBuilder();
        await using var command = new NpgsqlCommand("CREATE DATABASE " + quoting.QuoteIdentifier(name), admin);
        try { await command.ExecuteNonQueryAsync(); }
        catch (PostgresException ex) when (ex.SqlState == "42P04") { }
    }

    public static async Task SeedAsync(NpgsqlDataSource db, string connectionString, IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (!configuration.GetValue("Demo:SeedData", false)) return;
        Validate(connectionString, environment, true);
        await using var connection = await db.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(735181004);
            CREATE TABLE IF NOT EXISTS "DemoSeedHistory" ("Version" integer PRIMARY KEY);
            LOCK TABLE "Resumes", "Applications", "InterviewQuestions", "ApplicationStatusAudit" IN SHARE ROW EXCLUSIVE MODE;
            DO $seed$
            DECLARE resume_id uuid; i integer; app_id uuid;
            BEGIN
              IF EXISTS (SELECT 1 FROM "DemoSeedHistory") OR EXISTS (SELECT 1 FROM "Resumes")
                 OR EXISTS (SELECT 1 FROM "Applications") OR EXISTS (SELECT 1 FROM "InterviewQuestions")
                 OR EXISTS (SELECT 1 FROM "ApplicationStatusAudit") THEN RETURN; END IF;
              INSERT INTO "Resumes" ("Name", "Content", "WordCount", "IsDefault") VALUES
                ('Demo - Backend Engineer', E'# Alex Morgan (fictional)\n## Summary\nBackend engineer with 6 years of experience.\n## Skills\nC#, .NET 10, ASP.NET Core, PostgreSQL, Redis, Docker, RabbitMQ\n## Experience\n2020–2026: Backend Engineer at Example Studio. Built REST APIs and distributed services.', 35, true),
                ('Demo - Full Stack Engineer', E'# Alex Morgan (fictional)\n## Skills\nC#, ASP.NET Core, Blazor, TypeScript, React, PostgreSQL\n## Experience\n2021–2026: Full stack development at Example Studio.', 23, false),
                ('Demo - Technical Lead', E'# Alex Morgan (fictional)\n## Skills\nC#, .NET, Docker, RabbitMQ, PostgreSQL, Azure\n## Experience\n2022–2026: Led a team building distributed services at Example Studio.', 26, false);
              UPDATE "Resumes" SET "WordCount" = cardinality(regexp_split_to_array(trim("Content"), E'\\s+'));
              FOR i IN 1..30 LOOP
                SELECT "Id" INTO resume_id FROM "Resumes" ORDER BY "IsDefault" DESC, "Name" LIMIT 1 OFFSET ((i-1)%3);
                INSERT INTO "Applications" ("CompanyName", "Position", "JobDescription", "JobUrl", "Source", "ResumeId", "Status", "AtsScore", "Notes", "AppliedAt", "CreatedAt", "Priority") VALUES (
                  (ARRAY['Atlas','Nova','Orbit','Cedar','Nimbus','Vertex','Harbor','Pixel','Summit','Lumen'])[(i-1)%10+1] || ' Demo ' || i,
                  (ARRAY['Senior .NET Developer','Backend Engineer','Full Stack Engineer','Technical Lead','Platform Engineer'])[(i-1)%5+1],
                  (ARRAY['Build REST APIs using C#, ASP.NET Core, PostgreSQL and Redis.','Design distributed services with .NET, RabbitMQ and Docker.','Build web applications using Blazor, React, TypeScript and C#.','Lead a team delivering .NET microservices. Requires 5 years of C# experience.','Operate services using Docker, Kubernetes, Azure and PostgreSQL.'])[(i-1)%5+1],
                  'https://example.com/jobs/' || i, (ARRAY['LinkedIn','Direct','Referral'])[(i-1)%3+1], resume_id,
                  (ARRAY['Saved','Applied','HRInterview','TechnicalInterview','FinalInterview','Offer','Rejected'])[(i-1)%7+1],
                  55 + (i*13)%44, 'Fictional demo record. ATS score is illustrative, not computed.',
                  CASE WHEN (i-1)%7=0 THEN NULL ELSE NOW()-i*INTERVAL '1 day' END, NOW()-i*INTERVAL '1 day', i-1)
                RETURNING "Id" INTO app_id;
                IF i <= 10 THEN
                  INSERT INTO "InterviewQuestions" ("ApplicationId", "Question", "QuestionType", "Hint") VALUES
                    (app_id, 'How would you design an idempotent REST endpoint?', 'Technical', 'Discuss retries and unique constraints.'),
                    (app_id, 'Describe a time you resolved a difficult production issue.', 'Behavioral', 'Use the STAR structure.');
                END IF;
              END LOOP;
              INSERT INTO "DemoSeedHistory" VALUES (1);
            END; $seed$;
            """, connection, tx);
        await command.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
}
