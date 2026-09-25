---
name: vapt-audit
description: Use when the user asks for a VAPT audit, VAPT compliance check, security audit, OWASP API Top 10 or ASVS assessment of this codebase, or a pass/fail security report against the public (or internal-admin) API endpoints. Runs static code analysis plus safe dynamic probes, then writes a dated compliance report to docs/security/ with per-rule pass/fail verdicts, evidence, and rule references.
---

# VAPT Audit — SBQR API

Audits this repo's API surface against **OWASP API Security Top 10 (2023)**
(`rules/owasp-api-top10.md`) and **OWASP ASVS 4.0.3, selected chapters**
(`rules/asvs-checks.md`), producing a pass/fail verdict **per rule × per
endpoint** with evidence (`file:line` or HTTP request/response) and
remediation guidance. Writes the report to `docs/security/`.

This skill is **report-only**: never modify source code during an audit, never
run destructive or exploit-grade tests, never start remediation unless the
user explicitly asks afterwards.

## Scope rules (read before running)

- Default scope = the **public API surface** (`v1.public` OpenAPI group) plus
  every **anonymously reachable infra endpoint** (health, OpenAPI documents,
  Scalar UI, `/` redirect). If an endpoint is reachable without credentials,
  it is in scope — that includes the anonymous `v1.internal-admin` OpenAPI
  document itself as a *finding surface*, even though admin endpoints are not
  in scope.
- A controller is internal **iff** it carries
  `[ApiExplorerSettings(GroupName = "internal")]` — never infer scope from the
  `/admin/` URL segment (`CryptoKeysController` is internal with no `/admin/`
  segment). Same rule as the postman-export skill: trust the doc split, not
  route text.
- Audit the **internal-admin surface only when the user explicitly asks**.
  If the user just says "audit", confirm scope via AskUserQuestion
  (public-only vs public + internal-admin).
- The line numbers and file paths inside the rule catalog are *hints from the
  last audit*, not truth. Re-verify every control from live code on each run.

## Workflow

### 1. Confirm scope, mode & client context

Ask (if not already stated):
- **Scope**: public (default) | public + internal-admin.
- **Mode**: static-only | static + dynamic probes. Dynamic needs the app
  running locally; offer to start it:
  ```
  dotnet run --project src/Host/SBQR.Api
  ```
  Default URL `http://localhost:5001` (verify against
  `src/Host/SBQR.Api/Properties/launchSettings.json`).
- **Client context** (drives CORS/CSRF/token rules): the API is consumed by
  **FI mobile apps** (`package_id` allow-listed) and **optionally by browser
  apps**. Record any change the user reports — grade API8 step 3/8 and ASVS
  V14.4 CORS / V2.2 public-client checks against the declared population.

### 2. Enumerate the endpoint inventory

Build the table: method, path, controller/file:line, auth attribute + policy,
OpenAPI group. Sources: controllers under `src/Modules/*/**/Controllers/` and
the minimal-API endpoints in `src/Host/SBQR.Api/Program.cs` (health, OpenAPI,
docs, `/`, dev-only `/admin/_routes`). The inventory goes into the report §2.

### 3. Static pass

Read **both** rule files in full, top to bottom:
- `.claude/skills/vapt-audit/rules/owasp-api-top10.md`
- `.claude/skills/vapt-audit/rules/asvs-checks.md`

For every rule: execute its detection steps against live code with
Glob/Grep/Read, and record evidence as `path:line` plus a one-line quote.
Never fill a verdict from memory or from a previous report.

### 4. Dynamic pass (safe probes) — static+dynamic mode only

```
python .claude/skills/vapt-audit/scripts/vapt_probe.py \
  --base-url http://localhost:5001 \
  --output C:\Users\Arif\AppData\Local\Temp\opencode\vapt-probe.json
```

The script is bounded and non-destructive: no valid credentials, no writes,
no exploit payloads. The rate-limit probe sends 12 bogus-credential requests
with a probe-only `client_id` (`vapt-probe`) — it burns only that bogus id's
rate-limit window, never a real client's. Merge every probe result into the
findings; treat `INCONCLUSIVE` probes as `WARN` with an explanation, not
silent drops.

### 5. Assign verdicts

Per rule × endpoint: **PASS** (control present & effective) | **FAIL**
(control absent/bypassable) | **WARN** (design limitation, accepted-risk
candidate, or inconclusive probe) | **N/A** (rule genuinely not applicable —
state why). Severity per finding: Critical / High / Medium / Low / Info.

**Charter-deferred FAILs.** Some FAILs hit controls the project charter
(`AGENTS.md` → "Explicitly out of scope") deliberately defers — e.g. TLS
termination, rate limiting beyond the token endpoint, SAST/SBOM gates. Grade
them honestly as **FAIL** (the report must stay valid for external auditors)
and annotate:

> `[charter-deferred: <what> — AGENTS.md "Explicitly out of scope"]`

Never downgrade a real violation to PASS/WARN because the charter ignores it.

### 6. Write the report

Copy `templates/report-template.md` → `docs/security/vapt-report-YYYY-MM-DD.md`
(today's date). Fill every section: executive summary with verdict counts,
endpoint inventory, rule × verdict scorecard matrix, detailed findings
(rule refs, severity, evidence, remediation, deferred annotations), passed
checks, methodology + limitations. Include the audited commit
(`git rev-parse HEAD`). **No secrets in the report** — redact tokens, keys,
passwords; reference secrets by location, never by value.

### 7. Debrief

Tell the user: verdict counts, every FAIL/WARN one-liner with its rule ref,
where the report was written. Then **stop** — offer remediation as a
follow-up task; do not start fixing.

## Verdict & severity rubric

| Verdict | Meaning |
|---|---|
| PASS | Control implemented and effective for that endpoint |
| FAIL | Rule violated — missing, bypassable, or misconfigured control |
| WARN | Real limitation but accepted-risk/design trade-off, or probe INCONCLUSIVE |
| N/A | Rule cannot apply (explain, e.g. "no file-upload endpoints → API upload rules") |

| Severity | Guide |
|---|---|
| Critical | Auth bypass, key/secret disclosure, remote code exec |
| High | Broken authorization on an endpoint, missing auth, trust-integrity break |
| Medium | Missing hardening that raises exploitability (headers, limits, info disclosure) |
| Low | Defense-in-depth gaps, minor info leaks |
| Info | Observations worth recording, no direct risk |

## Safety rules (hard)

1. Read-only w.r.t. source — the only files an audit writes are under
   `docs/security/` and the temp probe output.
2. Probes must come from `scripts/vapt_probe.py` only — do not improvise
   ad-hoc attack requests (no SQLi/XSS/path-traversal payloads, no fuzzing,
   no auth brute-force with real-looking credentials).
3. Never copy secret *values* into the report; cite `file:line` instead.
4. Do not touch `/etc/sbqr/sbqr.env`, the EC2 box, or any non-local instance.
   Probes target localhost only.

## Common mistakes

| Mistake | Fix |
|---|---|
| Auditing from a previous report's verdicts | Re-run every rule's detection steps from live code |
| Inferring internal scope from `/admin/` in the URL | Only `[ApiExplorerSettings(GroupName = "internal")]` marks internal |
| Skipping anonymous infra endpoints | `/openapi/*.json`, `/docs/*`, `/health/*` are attack surface too |
| Grading CORS "N/A — it's an API, not a website" while browser clients are declared | Grade per API8 step 3: explicit FI-origin allow-list required once browser clients ship; absence = FAIL at that point |
| Downgrading charter-deferred gaps to PASS | FAIL + `[charter-deferred: …]` annotation, always |
| Sending improvised "attack" requests to a live server | Use only `scripts/vapt_probe.py`, localhost only |
| Verdict without evidence | Every non-PASS verdict needs `file:line` or req/resp evidence |
| Probing a deployed (EC2) instance | Probes are localhost-only, no exceptions |
| Forgetting the git SHA in the report | `git rev-parse HEAD` goes in the header — audits must be reproducible |
