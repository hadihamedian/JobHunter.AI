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

Endpoint: `http://localhost:5065/mcp`. Every MCP request requires `Authorization: Bearer YOUR_GENERATED_SECRET`. Requests must come directly from loopback with a loopback Host header and no Origin header. This deliberately excludes remote clients and browser-direct calls. Do not expose the REST API, database or MCP endpoint to a public network. Existing REST routes are unauthenticated; the MCP bearer secret does not protect them. The supplied Docker Compose file publishes ports on all interfaces; bind them to loopback or firewall them if using Docker for dependencies. No Docker MCP support is claimed in this preview.

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
| get_job_analysis | Fresh ATS evaluation of one saved job with an explicit, linked, or default resume. No persistent analysis cache. |
| get_matching_jobs | Fresh ATS ranking for a page of up to five Saved records, using an explicit or default resume. Returns examined count, next offset and skipped IDs. Not a global ranking. |
| update_application_status | Write-disabled by default. Compare-and-set status only, with an atomic MCP audit insert. Does not send an application or change other fields. |

Example calls: `search_jobs(query: ".NET", limit: 10)`, `get_matching_jobs(minScore: 70, candidateLimit: 3)`, `get_job_analysis(applicationId: "<existing UUID>")`. For analysis the job must have a stored description, and Ollama must be running. AI scores are estimates and may be influenced by untrusted job text. Resume and description input limits are 16,000 characters each; model context may impose a lower limit. AI calls are serialized across MCP requests and bounded by a cancellation deadline; existing REST AI calls are not included in this gate.

## Write test (disposable database only)

The existing application repository's generic update overwrites several nullable fields. The MCP-specific status operation intentionally uses a separate atomic SQL statement to avoid those side effects. It creates an additive `ApplicationStatusAudit` table during existing database initialization; only MCP updates are audited in this preview.

After backing up your database, use a test record. Enable writes explicitly with `dotnet user-secrets set "Mcp:AllowWrites" "true" --project JobHunter.AI.Api` and restart. Call `update_application_status` with the actual ID, expected current status and new status. Confirm with the user before triggering any write from a client. Check the record in the Web App and verify its other fields are unchanged. Repeat with the old expected status and verify the write is rejected. Disable writes again and restart. This opt-in setting authorizes any client holding the token to call the write tool; it is not per-user approval or separate write credentials.

## Acceptance checklist (not executed when this patch was authored)

- [ ] Restore, build and existing + new unit tests pass.
- [ ] With MCP disabled, `/mcp` is absent and existing Web App workflows still work.
- [ ] With MCP enabled, missing/wrong bearer token returns 401; non-local requests or Origin headers return 403.
- [ ] The MCP client lists exactly five tools; DI dependencies and CancellationToken are not exposed as tool inputs.
- [ ] Search and history return bounded, correct pages; invalid parameters produce errors.
- [ ] ATS analysis matches the existing REST analysis behavior, subject to model variability.
- [ ] Matching ranks only the requested Saved page and reports skipped records.
- [ ] Status mutation is denied by default, preserves other fields when enabled, and rejects stale expected status.
- [ ] Verify the corresponding audit row and repeat against a disposable PostgreSQL database.

## Remaining before release

Integration and regression tests with real PostgreSQL and MCP clients, dependency compatibility verification, REST authentication and deployment hardening, separate write authorization, rate limits, prompt-injection testing, output size limits, persisted analysis/caching, full status history, packaging and registry registration. Remote OAuth is intentionally deferred until the existing unauthenticated REST surface is secured. Do not publish this preview as production-ready.
