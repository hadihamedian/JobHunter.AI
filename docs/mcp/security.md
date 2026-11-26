# MCP security model — local preview

## Deployment boundary

Use the native API directly on loopback. MCP rejects non-loopback peer addresses, non-loopback Host values, Origin, Forwarded and X-Forwarded-* headers. Compose binds published database/API/Web ports to IPv4 loopback; Docker MCP is not supported in this preview. Do not expose any service publicly or forward MCP through a tunnel/reverse proxy. The built-in Web App's Nginx proxy is for REST, not MCP. Local malware, untrusted clients, browser XSS and cloud model providers remain outside this trust boundary.

## Credentials and access

| Setting | Purpose | Default / constraints |
|---|---|---|
| `Mcp:Enabled` | Enable endpoint | false |
| `Mcp:Token` | Read bearer | Required if enabled; at least 32 characters; generate 64 hex |
| `Mcp:WriteToken` | Separate write bearer | Optional; exactly 64 hex, distinct from read and REST |
| `Mcp:AllowWrites` | Independent mutation switch | false; true requires write credential |
| `RestAuth:Token` | REST bearer | Separate 64 hex; missing denies REST with 503 |

Generate each secret independently. Use development User Secrets or environment variables (double underscore for nested keys); never commit credentials. Restart after changes. The REST middleware bypasses MCP and vice versa. Invalid credentials return 401, malformed configured credentials fail startup under the applicable validation rules. MCP checks SHA-256 hashes with fixed-time comparisons and attaches a role and identity per request. Multiple or oversized Authorization headers are rejected. Responses after the local gate are marked no-store. REST and MCP credentials are not interchangeable when configured separately.

Read tokens discover four tools; write tokens discover five. The SDK authorization filter and in-tool authenticated writer role check enforce mutation authorization independently of discovery. The write switch must also be enabled. Stateless transport prevents privileges being carried by an MCP session ID. Mutation uses an atomic compare-and-set with one audit row in `ApplicationStatusAudit`; only MCP status changes are audited. Client confirmation is recommended but not enforced by an approval UI. Revoke write credentials by removing them and restarting. See [setup guide](README.md#optional-status-updates).

## REST browser access

The Web App verifies the REST bearer before storing it in localStorage, and revalidates it on reload. Sign out clears stored and in-memory credentials; 401 invalidates the saved session. Temporary API failure does not delete the credential. LocalStorage is accessible to same-origin scripts and persists across browser restarts. Use a trusted personal browser; rotate the server token and restart to revoke it. This is not an expiring server session or multi-user identity. Compose obtains the REST credential from `JOBHUNTER_REST_TOKEN` in the launching environment; avoid sharing expanded Compose config output.

## Resource protection

| Setting / limit | Default | Allowed range |
|---|---:|---:|
| `Mcp:Limits:RequestsPerMinute` | 120 | 1–600 |
| `Mcp:Limits:AnalysisPerMinute` | 12 | 1–120 |
| `Mcp:Limits:MaxConcurrentRequests` | 8 | 1–32 |
| `Mcp:Limits:MaxBodyBytes` | 65536 | 1024–262144 |
| Concurrent MCP ATS calls | 1 | Fixed |
| Body upload deadline | 10 seconds | Fixed, cooperative |
| ATS deadline | 45 seconds | Fixed, cooperative |
| Read-tool typed JSON output | 128 KiB | Fixed |

Limits are shared across credentials in one process, not distributed or per-user. Requests passing the local gate, including invalid credentials, consume the request budget. Fixed windows allow boundary bursts; restart resets budgets; invalid-token floods can exhaust the local budget. No queues. Transport exhaustion returns HTTP 429 with Retry-After (60 seconds for rate, one second for concurrency). ATS limits return MCP tool errors, including for cached calls. REST is outside MCP limits but shares the existing analyzer gate. Actual POST bytes are counted (including chunked requests); 413 for oversize, 415 for unsupported content encoding, 408 for cooperative upload timeout. JSON-RPC parsing remains with the SDK. Read output is checked before SDK serialization; oversize returns a safe error, not truncated pages. This is not a hard wire-byte or memory allocation cap. Invalid startup limit settings fail when MCP is enabled.

## Untrusted text and data handling

Treat job descriptions, company names, URLs, notes and model-generated output as untrusted. Never follow embedded instructions or allow them to authorize a write. The ATS analyzer separates system instructions from JSON-encoded input and uses cache version ats-v4. It does not grant the model tool access. These mitigations do not guarantee model compliance. The local analyzer can run without sending inputs off-device, but third-party MCP clients may send tool results to cloud models. Review provider privacy settings, minimize data and avoid screenshots/logs containing real credentials. The MCP middleware does not log raw request bodies or credentials; universal redaction across all components is not guaranteed. Derived cached ATS data may be personal. Protect database files and backups.

## Acceptance evidence

- [x] Maintainer-reported Release build and full 84-test suite, zero skips/failures.
- [x] Automated read/write separation, disabled writes, stale update, atomic audit, REST isolation and stateless discovery.
- [x] Automated rate/concurrency budget checks, body-size/encoding rejection, output-size rejection and smaller-page recovery.
- [x] Synthetic stored instructions cannot grant write access under the read credential; mocked model message separation.
- [x] Inspector read calls and Copilot four-tool discovery plus actual search invocation (maintainer smoke tests).
- [ ] Successful current-release live ATS, real-model injection resistance and real-client write confirmation/approval workflow.
- [ ] Fresh full-install browser login/logout/reload and deployment verification for this release.
- [ ] Claude Desktop, DeepSeek Harness and ChatGPT compatibility (deferred).

No public-network security, OAuth, multi-tenancy, full audit coverage, production secret management, universal prompt-injection resistance or production readiness is claimed. Do not publish a publicly reachable endpoint on the basis of these tests. Report security issues privately to the maintainer without including live secrets or personal data.
