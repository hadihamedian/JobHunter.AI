# JobHunter.AI MCP — local preview

The MCP adapter runs in the existing ASP.NET Core API. It shares the application repository, resume repository and ATS analyzer with the Web App. No separate service, database, or model provider is required. MCP is disabled by default. This is a local single-user preview, not a public or production-ready deployment.

## Apply and build

From the solution root, on a clean feature branch, use `git apply --check JobHunter-MCP-stage1.patch` followed by `git apply JobHunter-MCP-stage1.patch` (use the actual path to the downloaded file). Alternatively copy the files from the source ZIP, after checking your local changes.

```sh
dotnet restore JobHunter.AI.slnx
dotnet build JobHunter.AI.slnx
dotnet test JobHunter.AI.slnx --no-build
```

Use the existing local PostgreSQL and Ollama settings. Stop any API container that occupies port 5065. Start the API directly on the host (not inside Docker or behind a reverse proxy). The existing Web App can continue using its usual local API address.

## Enable MCP locally

Generate a random secret using `python -c "import secrets; print(secrets.token_hex(32))"`. Never commit the secret or share it in screenshots. Run these commands from the solution root in a terminal. `dotnet user-secrets init` is only required if the API project has no UserSecretsId.

```sh
dotnet user-secrets init --project JobHunter.AI.Api
dotnet user-secrets set "Mcp:Token" "YOUR_GENERATED_SECRET" --project JobHunter.AI.Api
dotnet user-secrets set "Mcp:Enabled" "true" --project JobHunter.AI.Api
dotnet run --project JobHunter.AI.Api --launch-profile http
```

Endpoint: `http://localhost:5065/mcp`. Every MCP request requires `Authorization: Bearer YOUR_GENERATED_SECRET`. Requests must come directly from loopback with a loopback Host header and no Origin header. This deliberately excludes remote clients and browser-direct calls. Do not expose the REST API, database or MCP endpoint to a public network. REST uses a separate local bearer token (see security.md); the MCP bearer secret does not protect REST. Docker Compose binds published ports to loopback. No Docker MCP support is claimed in this preview.

## Connect an MCP client (VS Code example)

Create `.vscode/mcp.json` locally (do not store the actual secret in it):

```json
{
  "inputs": [{"id":"jobhunter-token","type":"promptString","description":"JobHunter MCP token","password":true}],
  "servers": {
    "jobhunter": {
      "type":"http",
      "url":"http://localhost:5065/mcp",
      "headers":{"Authorization":"Bearer ${input:jobhunter-token}"}
    }
  }
}
```

Start the server from the MCP configuration and inspect its tool list. Client compatibility must be confirmed on your installation. MCP Inspector can also be used with Streamable HTTP and the same bearer header, if its proxy sends no Origin header.

## Tools

| Tool | Behavior |
|---|---|
| search_jobs | Keyword and optional status search of existing application records, page size 1–100. No internet search. |
| get_application_history | List existing application records; not a status-event timeline. |
| get_job_analysis | ATS evaluation of one saved job with an explicit, linked, or default resume; reuses a valid cached result. |
| get_matching_jobs | ATS ranking (cached or freshly computed) for a page of up to five Saved records, using an explicit or default resume. Returns examined count, next offset and skipped IDs. Not a global ranking. |
| update_application_status | Write-disabled by default. Compare-and-set status only, with an atomic MCP audit insert. Does not send an application or change other fields. |

Example calls: `search_jobs(query: ".NET", limit: 10)`, `get_matching_jobs(minScore: 70, candidateLimit: 3)`, `get_job_analysis(applicationId: "<existing UUID>")`. For analysis the job must have a stored description, and Ollama must be running. AI scores are estimates and may be influenced by untrusted job text. Resume and description input limits are 16,000 characters each; model context may impose a lower limit. ATS cache misses are serialized across REST and MCP in one API process. MCP analysis uses a cooperative 45-second deadline. Other AI features do not use this ATS gate.

## Shared ATS analysis cache

The existing REST analysis endpoints and both AI-heavy MCP tools share a PostgreSQL-backed cache via `AiAnalyzerService`. No additional infrastructure is required. The additive `AtsAnalysisCache` table and expiry index are initialized at startup. Cache initialization failure disables caching until restart, without blocking the existing analyzer. Read/write failures fall back to normal analysis. Caller cancellation is propagated.

- Defaults: `AtsCache:Enabled=true`, `AtsCache:TtlHours=24` (valid range 1–168 hours).
- Cache keys hash the exact resume and job description, effective Ollama endpoint/model/context window, explicit `AtsCache:ModelRevision`, and `AtsAnalysisCache.AlgorithmVersion`. No full raw resume or job description is added to the cache table; derived ATS results may still contain personal data.
- If model weights change under the same Ollama tag, change `AtsCache:ModelRevision` and restart. Bump `AlgorithmVersion` when changing ATS prompts, skill matching or scoring. TTL is measured from the successful write, not extended by reads.
- Null/malformed results and out-of-range scores are not cached. This does not validate the semantic accuracy of a model-generated score; quality fixes are separate.
- Expired rows are excluded from reads and physically removed at startup or a subsequent successful write (not by a background scheduler). The cache is scoped to the existing single-user deployment; tenant isolation and deletion/retention policy are required before multi-user use.
- The shared ATS gate rechecks cache after acquiring the lock. Concurrent misses with identical inputs reuse successful cached results when caching works. Cold analysis can still exceed the budget if the provider delays cancellation.

To disable caching: `dotnet user-secrets set "AtsCache:Enabled" "false" --project JobHunter.AI.Api` and restart. To validate, call `get_job_analysis` twice with the same valid job and resume, using a sufficiently fast Ollama model. Expect `ATS cache MISS` then `ATS cache STORED` for the first call and `ATS cache HIT` for the second. If a client times out, check server logs and the configured client deadline; do not repeatedly submit slow requests. Confirm a cache hit after API restart. On a disposable record, change the description and confirm a fresh miss.

Inspect metadata only (avoid exposing derived personal data):

```sql
SELECT "CreatedAt", "ExpiresAt" FROM "AtsAnalysisCache"
ORDER BY "CreatedAt" DESC LIMIT 5;
```

## Stage 3 reliability

ATS v2 uses equal-weight coverage of extracted requirements, rounded down. The model no longer determines the numeric score. Unknown or contradictory experience classifications count as missing; exact duplicates are removed. Skill-only analysis avoids the second model call and produces fallback suggestions for gaps. Mandatory skill classification is unavailable for skill-only jobs (critical list is empty). This is an estimate, not a calibrated employer ATS score. Old cache keys are invalidated, but historical stored application scores are not rewritten.

MCP matching returns partial results with `timedOut`, `message`, and `nextOffset` when its cooperative 45-second budget expires. Retry from the returned offset; if no candidates complete, use a faster model or pre-analyze in the Web App with the same resume. The REST analysis calls now propagate request cancellation. Expected validation errors use McpException with safe messages; unexpected infrastructure errors remain generic. No background processing or cross-process deduplication is provided.

## Write test (disposable database only)

The existing application repository's generic update overwrites several nullable fields. The MCP-specific status operation intentionally uses a separate atomic SQL statement to avoid those side effects. It creates an additive `ApplicationStatusAudit` table during existing database initialization; only MCP updates are audited in this preview.

After backing up your database, use a disposable test record. Configure an independent `Mcp:WriteToken` (see Stage 6.3 below), explicitly enable `Mcp:AllowWrites`, and restart. Call `update_application_status` using the write token with the actual ID, expected current status and new status. Confirm with the user before triggering any write from a client. Check other application fields remain unchanged and the audit row exists. Repeat with the stale expected status to confirm no second write. Disable writes again and restart. A read token must never authorize this operation.

## Acceptance checklist (not executed when this patch was authored)

- [ ] Restore, build and existing + new unit tests pass.
- [ ] With MCP disabled, `/mcp` is absent and existing Web App workflows still work.
- [ ] With MCP enabled, missing/wrong bearer token returns 401; non-local requests or Origin headers return 403.
- [ ] The read-only MCP client lists four read tools; the write-authorized client lists all five. DI dependencies, ClaimsPrincipal and CancellationToken are not tool inputs.
- [ ] Search and history return bounded, correct pages; invalid parameters produce errors.
- [ ] ATS analysis matches the existing REST analysis behavior, subject to model variability.
- [ ] Matching ranks only the requested Saved page and reports skipped records.
- [ ] Status mutation is denied by default, preserves other fields when enabled, and rejects stale expected status.
- [ ] Verify the corresponding audit row and repeat against a disposable PostgreSQL database.

## Remaining before release

Real-client compatibility, rate limits, prompt-injection testing, output size limits, ATS quality improvements, cold-analysis timeout handling, full status history, packaging and registry registration remain before release. REST authentication and MCP read/write credentials are separate local protections, not remote OAuth or production-ready multi-user security. Do not expose this local preview publicly.

## Stage 3.1 refinements

ATS v3 excludes requirements without letters (including bare numeric thresholds); the extractor is instructed to keep the full experience requirement. Malformed numeric-only thresholds are discarded rather than guessed. For .NET Core requirements, explicit modern .NET 5+ and ASP.NET Core are accepted as heuristic evidence, not proof of exact runtime version; .NET Framework alone does not qualify. Cache keys change to ats-v3. The matching loop has controlled cancellation tests for partial results and caller cancellation; real PostgreSQL/Ollama timeout acceptance remains separate.

## Persian output readability

MCP tool serialization uses a copy of the SDK default JSON options with a restricted Unicode encoder allowing Arabic-script text (including Persian). Property names, tool contracts and security-sensitive escaping are retained. This does not translate content or change REST serialization. Some characters (including zero-width marks) may still be escaped, and the transport envelope or Inspector may independently escape Unicode. Escaped JSON is semantically equivalent to literal Unicode. Verify by searching a stored record with a Persian company name and checking its decoded value; test an English record and tool discovery as regression checks.


## Stage 5: automated integration tests

The integration tests start a real loopback Kestrel host with the production MCP extensions, repositories and ATS cache. They use raw MCP Streamable HTTP JSON-RPC requests (initialize, session headers, tools/list and tools/call). The host reproduces the relevant registrations, not the entire Program.cs startup. AI responses are deterministic mocks; neither Ollama nor the existing application database is required. Existing controlled timeout/cancellation unit tests remain separate; live Ollama cancellation and full application startup regression still require acceptance tests.

From Windows CMD at the solution root, start a dedicated disposable PostgreSQL container:

```cmd
docker run --name jobhunter-integration-pg -e POSTGRES_PASSWORD=integration-only -e POSTGRES_DB=jobhunter_integration -p 127.0.0.1:55432:5432 -d postgres:16
docker exec jobhunter-integration-pg pg_isready -U postgres -d jobhunter_integration
set "JOBHUNTER_INTEGRATION_DB=Host=127.0.0.1;Port=55432;Database=jobhunter_integration;Username=postgres;Password=integration-only"
dotnet test JobHunter.AI.slnx
```

Wait for PostgreSQL readiness before running tests. Each test uses its own random schema, which is dropped on normal fixture disposal. The database name must be `jobhunter_integration` and host must be loopback. Never point this variable at a real data store. Without the environment variable, integration tests are explicitly skipped and unit tests still run. GitHub Actions provisions a separate PostgreSQL 16 service and explicitly runs both suites. Local cleanup: `docker rm -f jobhunter-integration-pg` and `set JOBHUNTER_INTEGRATION_DB=`. Only remove the dedicated test container. No user secrets changes are needed.

Coverage: missing/wrong token, Origin and Host rejection, disabled endpoint, tool discovery and search input schema, Persian data round-trip, search/history paging, write denial, successful and stale status changes with field preservation and audit rows, real PostgreSQL cache hit, description invalidation, expiration and cleanup, matching with incomplete records. Live provider accuracy, remote network tests, concurrent mutation races, and full production security remain outside this suite.

## Stage 6.3 — MCP write authorization (local-only)

This stage targets `ModelContextProtocol.AspNetCore` **2.4.0** and explicitly pins Streamable HTTP to **stateless** mode. Each HTTP request is authenticated independently. `Mcp:Token` remains the existing backward-compatible read token; a separate `Mcp:WriteToken` (exactly 64 hexadecimal characters) grants write-role access. The REST bearer is a third, distinct credential. All credentials are checked server-side; they must not be stored in source control.

Generate a NEW credential (do **not** reuse either the REST token or `Mcp:Token`):

```cmd
python -c "import secrets; print(secrets.token_hex(32))"
dotnet user-secrets set "Mcp:WriteToken" "PASTE_NEW_64_HEX_HERE" --project JobHunter.AI.Api
dotnet user-secrets set "Mcp:AllowWrites" "true" --project JobHunter.AI.Api
```

Restart the API after configuring credentials. Keep `Mcp:AllowWrites=false` unless a controlled write test or personal workflow needs it. When `Mcp:AllowWrites=true` without `Mcp:WriteToken`, startup fails. Malformed or reused write credentials also fail startup. A configured write token can read even when writes are disabled, but cannot mutate until the explicit flag is enabled. To disable writes: `dotnet user-secrets set "Mcp:AllowWrites" "false" --project JobHunter.AI.Api`, then restart. Removing `Mcp:WriteToken` and restarting revokes the write credential. The original read credential continues to work for read operations.

For a write-capable MCP client, use the same local `/mcp` endpoint and send `Authorization: Bearer <Mcp:WriteToken>` instead of the read token. Do not put real credentials in committed MCP client settings. Read-token clients discover only four read tools; write-token clients discover all five. The write-only tool `update_application_status` is guarded by the SDK authorization filter, an independent in-tool role check, and `Mcp:AllowWrites`. Its atomic compare-and-set and audit record remain unchanged. Tool discovery is not itself authorization.

The SDK is configured without HTTP sessions, so an `Mcp-Session-Id` cannot carry or elevate privileges. The SDK's request identity is derived from the currently authenticated bearer credential; no write privilege is persisted in a session. Stateless operation does not imply OAuth, remote access, or automatic approval of mutations. Each requested status change still needs an explicit expected status and the client's informed confirmation.

Stage 6.3 regression checks: read token can call `search_jobs` and is denied on `update_application_status`, even with `AllowWrites=true`; write token cannot mutate with `AllowWrites=false`; valid write token succeeds after opt-in; stale status does not create a second audit row; REST credentials are rejected by MCP and vice versa; no session identifier is issued. Test with the disposable PostgreSQL setup above, not a real application database.
