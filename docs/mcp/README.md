# JobHunter.AI MCP — local preview

Use your existing job tracker from an MCP-compatible AI client. The ASP.NET Core API hosts the MCP adapter alongside REST, sharing PostgreSQL, resume/application repositories and the local Ollama ATS analyzer. No second API or database is required.

**Scope:** single user, direct loopback, stateless Streamable HTTP (SDK 2.4.0). MCP is disabled by default. Public source distribution does not mean public endpoint hosting.

## Quick start (Windows CMD)

Install .NET 10 SDK, PostgreSQL 16, Ollama and Python 3 (for secret generation). Node.js/npm is needed for Inspector. Follow the [main setup guide](../../README.md) for the database and Web App. Start Ollama and pull the configured model (`ollama pull llama3.2:latest`). If using Compose for PostgreSQL only, `docker compose up -d postgres` is sufficient; stop any API/Web containers occupying ports 5065/5150 before starting the native apps. Do not remove existing database volumes. Back up existing data before upgrading: startup initializes additive tables.

From the solution root, generate **two different** 64-character hexadecimal secrets, using the command twice. Do not reuse a credential or share its value.

```cmd
python -c "import secrets; print(secrets.token_hex(32))"
python -c "import secrets; print(secrets.token_hex(32))"
dotnet user-secrets set "RestAuth:Token" "REPLACE_WITH_REST_SECRET" --project JobHunter.AI.Api
dotnet user-secrets set "Mcp:Token" "REPLACE_WITH_READ_SECRET" --project JobHunter.AI.Api
dotnet user-secrets set "Mcp:Enabled" "true" --project JobHunter.AI.Api
dotnet user-secrets set "Mcp:AllowWrites" "false" --project JobHunter.AI.Api
dotnet run --project JobHunter.AI.Api --launch-profile http
```

User Secrets are for development, not an encrypted production vault. The project already has a UserSecretsId. Keep the API at `http://localhost:5065`, the native Web App at `http://localhost:5150`, and PostgreSQL/Ollama on the local machine. Start the Web App in another CMD with `dotnet run --project JobHunter.AI.Web --launch-profile http`; enter the REST token when prompted. Existing users need not replace working tokens just to enable MCP. Restart the API after changing credentials or resource settings.

## Connect a client

Endpoint: `http://localhost:5065/mcp`. Every request needs `Authorization: Bearer <read-token>`. Use read access by default. Direct loopback requests only; no Origin or forwarded headers. Do not open firewall ports or use a tunnel or reverse proxy for MCP. External AI clients may send tool output to cloud providers; review their privacy settings and use synthetic records for experiments.

### MCP Inspector

Run `npx -y @modelcontextprotocol/inspector` (review the npm package/version before executing). Open the local UI, select Streamable HTTP, enter the endpoint, and use the local proxy with an Authorization header containing `Bearer ` followed by the read token. Connect and list tools. Four tools should appear. Do not confuse the Inspector proxy's own authentication token with the JobHunter credential. Keep the proxy local. UI labels vary by version.

### VS Code / GitHub Copilot

Use the MCP command palette to add an HTTP server named `jobhunter`. For versions using Copilot Global configuration (`%USERPROFILE%\.copilot\mcp-config.json`), the following is the configuration form used in the maintainer smoke test. Merge the entry with existing settings, rather than overwriting other servers:

```json
{
  "mcpServers": {
    "jobhunter": {
      "type": "http",
      "url": "http://localhost:5065/mcp",
      "headers": { "Authorization": "Bearer ${JOBHUNTER_MCP_TOKEN}" },
      "tools": ["*"]
    }
  }
}
```

Close all VS Code processes, then launch from CMD with `set "JOBHUNTER_MCP_TOKEN=REPLACE_WITH_READ_SECRET"` followed by `code .`. Start jobhunter from `MCP: List Servers`; inspect discovery and verify a tool call in Copilot Chat. Do not commit credentials. Environment-variable substitution depends on client version. For older VS Code configurations using `mcp.json`, use its native `servers` and secret `inputs` format; do not mix the two formats. See the client's own documentation if menus differ.

Example prompt: **Use JobHunter's search_jobs tool to find up to five Saved .NET applications and summarize them. Do not change anything.** Check the actual tool invocation, not just generated text.

## Tool reference

| Tool | Arguments | Result / scope |
|---|---|---|
| `search_jobs` | `query?`, `status?`, `limit=20`, `offset=0` | Local records; searches company, position, notes and description |
| `get_application_history` | `status?`, `limit=20`, `offset=0` | Current stored application records, not a status-event timeline |
| `get_job_analysis` | `applicationId`, `resumeId?` | ATS score, matching/missing skills, critical missing items, suggestions; uses explicit, linked or default resume |
| `get_matching_jobs` | `resumeId?`, `minScore=60`, `candidateLimit=5`, `offset=0` | Rank only the requested Saved page using explicit or default resume; includes examined count, skipped IDs, next offset and timeout status |
| `update_application_status` | `applicationId`, `expectedStatus`, `newStatus` | Optional write: compare-and-set status and atomic audit; returns updated flag and message |

IDs are UUIDs. Status values: `Saved`, `Applied`, `HRInterview`, `TechnicalInterview`, `FinalInterview`, `Offer`, `Rejected`. Search/history limits: 1–100 records, offset 0–100000, query at most 300 characters. Ordered by priority ascending, creation time descending, then ID. Matching examines 1–5 candidates, score threshold 0–100. It is not a global ranking or web search. Analysis needs a stored description and a nonempty resume, each at most 16000 characters; model context can impose a smaller effective limit. Missing descriptions must be added in the Web App. A default resume must be unique when no explicit/linked ID applies. Matching can return partial results; resume at `nextOffset` if appropriate. No tool submits a job application.

## Optional status updates

Keep writes disabled for normal usage. Generate an independent new 64-hex token and set:

```cmd
python -c "import secrets; print(secrets.token_hex(32))"
dotnet user-secrets set "Mcp:WriteToken" "REPLACE_WITH_WRITE_SECRET" --project JobHunter.AI.Api
dotnet user-secrets set "Mcp:AllowWrites" "true" --project JobHunter.AI.Api
```

Restart and configure a trusted client with the write token instead of the read token. It discovers five tools even when the write switch is false; actual mutation requires both authorization and opt-in. Ask for explicit confirmation before a write; the server has no approval UI. Test only on disposable records. A stale expected status returns `updated=false` without an audit insert. Disable writes using `dotnet user-secrets set "Mcp:AllowWrites" "false" --project JobHunter.AI.Api`; remove `Mcp:WriteToken` via `dotnet user-secrets remove "Mcp:WriteToken" --project JobHunter.AI.Api` and restart to revoke it. Existing read access is unaffected.

## ATS and cache behavior

The shared REST/MCP ATS analyzer uses model-assisted requirement extraction and experience classification, deterministic skill checks and equal-weight scoring rounded down. It is an estimate, not an employer ATS score. Ambiguous experience counts as missing; duplicate requirements and bare numeric thresholds are discarded. Modern .NET aliases are heuristic. Skill-only analysis avoids a second model call and has no mandatory-skill classification. Job/resume data are separated from system instructions; injection resistance is not guaranteed.

Persistent PostgreSQL cache defaults: `AtsCache:Enabled=true`, `AtsCache:TtlHours=24` (1–168). Keys include exact inputs, endpoint/model/context, `AtsCache:ModelRevision` and algorithm version `ats-v4`. Update ModelRevision if weights change under the same tag. Old entries are not reused after an algorithm change; historical application scores are not rewritten. Expired rows are removed at startup or successful subsequent cache writes. Failures fall back to analysis, and startup cache failure disables caching until restart. Derived results can contain personal data. Concurrent misses are serialized by a shared in-process gate. MCP analysis has a cooperative 45-second deadline; slow providers may exceed it. No background processing or cross-process deduplication.

## Troubleshooting

| Symptom | Action |
|---|---|
| 404 | Enable MCP and restart; verify API port |
| 401 | Verify correct read/write bearer header; REST credential is separate |
| 403 | Check loopback Host, direct connection, Origin/forwarded headers |
| 413 / 415 / 408 | Reduce body size / remove encoding / retry with a normal upload |
| 429 | Respect Retry-After; avoid repeated immediate retries |
| ATS busy/rate error | Wait for the existing analysis or retry after the stated delay |
| Output budget error | Reduce page size or shorten stored fields |
| Missing description/resume | Populate records in the Web App |
| Slow analysis | Use a faster model or pre-analyze with the same inputs in the Web App |
| Persian escapes | JSON Unicode escapes are equivalent to decoded text; the client may escape independently |

See [security.md](security.md) for limits and privacy details.

## Automated tests

From repository root, with Docker running, use a dedicated disposable PostgreSQL database (reuse the named test container if already created):

```cmd
docker run --name jobhunter-integration-pg -e POSTGRES_PASSWORD=integration-only -e POSTGRES_DB=jobhunter_integration -p 127.0.0.1:55432:5432 -d postgres:16
docker exec jobhunter-integration-pg pg_isready -U postgres -d jobhunter_integration
set "JOBHUNTER_INTEGRATION_DB=Host=127.0.0.1;Port=55432;Database=jobhunter_integration;Username=postgres;Password=integration-only"
dotnet build JobHunter.AI.slnx -c Release
dotnet test JobHunter.AI.slnx -c Release --no-build
```

Wait for readiness. Tests use isolated random schemas, cleaned up on normal disposal, real loopback Kestrel/PostgreSQL and mocked AI. They do not use your actual application data or full Program.cs startup. Without the variable, integration tests are skipped. CI provisions PostgreSQL 16. Cleanup only the disposable container (`docker rm -f jobhunter-integration-pg`) and clear the variable (`set JOBHUNTER_INTEGRATION_DB=`).

## Compatibility and release status

Maintainer-reported validation: 84 tests passed, zero failures/skips, Release build successful; Inspector read calls and VS Code/GitHub Copilot four-tool discovery and search invocation verified. Successful live ATS with the current release, real-client mutation/approval, real-model injection resistance, Claude Desktop, DeepSeek Harness and ChatGPT are not established by this evidence. See [release notes](release-notes.md). This is a local preview, not a remote production deployment.
