# MCP local preview — release notes

**Proposed release label:** MCP preview 1 (tag suggestion: `mcp-v0.1.0-preview.1`). Not yet published; the maintainer must select the final tag and release target.

## What's new

- Optional stateless Streamable HTTP MCP endpoint in the existing .NET 10 API, SDK 2.4.0.
- Four read tools for saved jobs, application records and ATS analysis; one optional status mutation tool with atomic compare-and-set and audit.
- Shared persistent ATS cache, deterministic skill verification and bounded matching with partial timeout results.
- Independent REST/read/write credentials, local transport gate, rate/concurrency/input/output budgets, separated ATS system instructions and untrusted data.
- Persian-friendly tool serialization and PostgreSQL-backed automated integration tests.

## Upgrade notes

Back up PostgreSQL before upgrading. Startup creates additive audit/cache tables. REST now requires a separate bearer; Docker Web App users must set JOBHUNTER_REST_TOKEN before starting Compose. Existing MCP read credentials remain compatible; writes now require a separate write token and explicit switch. ATS cache keys use ats-v4; prior cached analyses are not reused, but application scores are not rewritten. Docker published ports bind to loopback. Follow the [setup guide](README.md) and [security model](security.md).

## Validation and known limitations

Maintainer-reported results: Release build succeeded; 84 automated tests passed with zero failures and skips. Inspector read smoke tests and VS Code/GitHub Copilot discovery/search succeeded. The Inspector analysis attempt returned the expected missing-description validation error; successful live analysis on the current build was not established in that session. One Copilot call failed before later successful calls; cause not established. Real-client writes, current-release live ATS, other clients and public deployment are not certified. Cloud clients may transmit records to their model providers. MCP is not a web job search service and does not submit applications. Registry publication and distribution packaging are separate follow-up work.

## Release checklist

- [x] Development and automated acceptance through Stage 6.5 (maintainer-reported).
- [x] Required read-only client smoke tests (Stage 7 scope).
- [ ] Review and commit documentation, check PR diff for secrets/personal records.
- [ ] Open PR to the repository's actual default branch; verify required CI checks before merging.
- [ ] Confirm tag does not already exist, create the chosen tag on the merged commit and publish GitHub release as a pre-release.
- [ ] Record Registry/distribution prerequisites separately; no registry listing is claimed.

No runtime code changes are included in the documentation update.
