# Stage 6.1 — local deployment hardening

Compose publishes PostgreSQL, API and Web only on IPv4 loopback. Recreate existing containers to apply the new bindings. This is not a public production deployment. Do not configure a reverse proxy, tunnel, or public listener for these services. Stage 6.1 alone did not authenticate REST; Stage 6.2 below adds a separate credential. MCP rejects Origin, Forwarded and X-Forwarded-* headers and multiple or oversized Authorization values. Authenticated MCP responses are marked no-store (the header is also applied to authentication failures after the local check). Keep MCP writes disabled except during controlled tests. Existing REST authentication, remote OAuth, rate limits, least-privilege write scopes and deployment secret management are separate release blockers. The network bindings may prevent access from other devices or VMs that previously used the host IP.

Validate with `docker compose config`, recreate running services with `docker compose up -d`, and check published ports with `docker compose ps`. Run unit and integration tests against the disposable database as documented in README. Confirm the existing local Web App and MCP Inspector still connect; ensure a request with a Forwarded or X-Forwarded-For header returns 403. No remote production readiness is claimed.

## Stage 6.2 — single-user REST access

Generate a separate random 64-character hexadecimal token with `python -c "import secrets; print(secrets.token_hex(32))"`. For native Development API, configure `dotnet user-secrets set "RestAuth:Token" "<generated token>" --project JobHunter.AI.Api` and restart. For Compose, set `JOBHUNTER_REST_TOKEN` in the launching shell and recreate the API. Do not publish the token, and avoid sharing Compose config output containing it. Never reuse the MCP token. Missing REST configuration denies REST requests with 503; malformed or identical credentials fail startup. MCP remains separately authenticated.

Open the Web App at http://localhost:5150 and enter the REST token. The REST token is stored in browser localStorage after successful verification and restored only after a new API session check. Sign out removes the stored token and clears the in-memory HTTP client credential; reload retains login when the API accepts the saved token. A 401 invalidates the saved session; temporary connection failures do not delete the saved credential. LocalStorage is readable by same-origin scripts and persists across browser restarts; use a trusted personal browser only, never a shared device or public deployment. To recover from temporary API failure, reload after the API is available. rotate the server token and restart to revoke the credential server-side. This is not an expiring server session or multi-user identity system. Use only trusted local browser and loopback HTTP; do not expose remotely or use a public proxy. XSS or a compromised local process can still access the credential. All existing non-MCP API paths require the token; valid CORS preflight is handled by existing CORS middleware. Clients must send Authorization: Bearer <token>. A missing or wrong token is denied before repositories or AI run.

Manual checks: anonymous REST request returns 401 (503 if unconfigured); wrong token returns 401; valid token unlocks the Web App and existing operations; reload restores login, Sign out requires token entry again, a rejected saved token is removed; REST token is rejected by MCP. Existing MCP credentials and Inspector behavior remain unchanged. Unit/HTTP tests do not establish browser end-to-end compatibility or remote deployment readiness.

## Stage 6.3 — MCP read/write authorization

- `Mcp:Token` is the existing read credential. Its format is unchanged for backward compatibility.
- `Mcp:WriteToken` is a separate 64-character hexadecimal credential, not shared with `Mcp:Token` or `RestAuth:Token`. Configure via User Secrets or environment variables, never source files. If malformed or duplicated, MCP startup fails.
- `Mcp:AllowWrites` is an independent kill switch. Setting it to true without a valid separate write token fails startup. Leave it false except during explicit write usage.
- The MCP transport authenticates **every** HTTP request (fixed-time SHA-256 comparisons), attaches either the `McpReader` or `McpWriter` role to the request principal, and rejects wrong credentials. The REST middleware still bypasses `/mcp`, while the MCP middleware ignores all other paths.
- With SDK 2.4.0, `AddAuthorizationFilters()` enforces `[Authorize(Roles = "McpWriter")]` on `update_application_status`; unauthorized callers do not see the tool during discovery and cannot invoke it. The tool also checks the authenticated writer role and `AllowWrites` before it accesses the repository.
- Streamable HTTP is explicitly stateless; an MCP HTTP session cannot be used to replay or upgrade authorization. `ClaimTypes.NameIdentifier` is distinct for read and write credentials as additional identity separation.
- Read token clients get four tools; write token clients get five. Possession of the write token is powerful: protect it like a password. No per-user OAuth, approval UI, network exposure, fine-grained scopes, or remote production assurances are added.

Acceptance tests must include invalid credential, disabled-write, read-token write denial after writes are enabled, write-token success, stale compare-and-set, one audit row only, REST/MCP credential separation, and no `Mcp-Session-Id` from the API. Integration tests run only against the dedicated `jobhunter_integration` PostgreSQL database.

## Stage 6.4 — MCP resource protection

Local, single-process limits are shared across read and write credentials. All MCP requests passing the local/Host/header gate (including failed authentication) consume one fixed-window request permit. No raw token is used as a partition key. Default: 120 requests/minute, eight concurrent authenticated transport requests, 12 ATS tool calls/minute, one concurrent MCP ATS tool call. No queues. Request/concurrency exhaustion returns HTTP 429 with Retry-After (60 seconds for the rate budget, one second as a concurrency retry hint). ATS exhaustion returns a safe MCP tool error instead; the transport may already be streaming. Cached ATS calls count too; matching still examines at most five candidates. The existing shared REST/MCP analyzer gate is retained, and REST is not subject to these MCP limits.

Authenticated MCP POST bodies are limited to 65,536 actual bytes, including chunked transfer; oversized requests return 413, unsupported Content-Encoding returns 415, and a cooperative ten-second upload deadline returns 408. JSON-RPC parsing and errors remain the SDK's responsibility. The middleware does not log bodies or credentials. Existing Kestrel header and transport limits continue to apply. These are not public-network DoS defenses or output-size limits. Fixed windows allow bursts near a boundary; budgets reset on process restart, are shared by trusted local clients, and an invalid-token flood can exhaust the local budget.

Optional startup configuration (restart required): `Mcp:Limits:RequestsPerMinute` (1–600), `Mcp:Limits:AnalysisPerMinute` (1–120), `Mcp:Limits:MaxConcurrentRequests` (1–32), `Mcp:Limits:MaxBodyBytes` (1024–262144). Invalid values fail startup when MCP is enabled. Do not raise limits without measuring local resource usage. The existing 45-second cooperative analysis deadline and field/page limits remain unchanged.


## Stage 6.5 — security acceptance

Read-tool typed JSON payloads are checked against a 128 KiB UTF-8 budget before SDK serialization. Oversized results return a safe tool error; no silent truncation or altered pagination. Reduce page size or shorten stored data. This is not a hard wire-byte or allocation cap: the repository and serialization may allocate the full result before checking, and the SDK envelope adds overhead. The fixed-size write result does not need this check.

ATS prompts separate trusted system instructions from JSON-encoded untrusted job/resume data. The shared REST/MCP analyzer is affected; cache algorithm version is bumped to ats-v4 so old prompt results are not reused. No model tool access is added. This reduces injection risk but cannot guarantee model compliance. MCP clients must treat all tool data as untrusted, never follow embedded instructions, and obtain explicit approval for writes; use the read credential by default. Stored text is not censored or rewritten.

Acceptance checklist (mark only after execution):
- [ ] Release build and full unit/integration suite with zero skips against disposable PostgreSQL.
- [ ] Read/write separation, disabled writes, compare-and-set and audit, REST token isolation, no session privilege carry.
- [ ] HTTP body/rate/concurrency controls and safe oversized-output error; smaller page still succeeds.
- [ ] Synthetic stored prompt injection leaves records/audit unchanged under read credential; verify real model and real client separately.
- [ ] Verify real client discovery and schema, read calls, explicit approval before any write, and inspect safe logs without sharing credentials.
- [ ] Verify loopback-only bindings and REST browser login/logout/reload after native API startup.

Mocked model tests establish message separation and server authorization, not real-model resistance or universal secret redaction. No public deployment, OAuth, or multi-tenant security is claimed. Live client checks remain Stage 7; do not mark them passed from automated tests alone.
