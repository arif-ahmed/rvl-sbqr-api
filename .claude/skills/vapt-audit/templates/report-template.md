# VAPT Compliance Report — {{SCOPE}} Endpoints

> Fill every `{{PLACEHOLDER}}`. Delete this blockquote when done.

| Field | Value |
|---|---|
| Report date | {{YYYY-MM-DD}} |
| Audited commit | `{{git rev-parse HEAD}}` |
| Scope | {{public \| public + internal-admin}} |
| Client population | {{FI mobile apps (package_id allow-listed); browser apps (optional)}} |
| Mode | {{static-only \| static + dynamic}} |
| Rule sets | OWASP API Security Top 10 (2023); OWASP ASVS 4.0.3 (selected chapters) |
| Auditor | Agent-run `vapt-audit` skill |
| Probe output | {{path to raw JSON, or "not run (static-only)"}} |

## 1. Executive summary

{{2–4 sentences: overall posture, verdict counts, the single most important
risk.}}

| Verdict | Count |
|---|---|
| FAIL | {{n}} |
| WARN | {{n}} |
| PASS | {{n}} |
| N/A | {{n}} |

Failures annotated `[charter-deferred: …]` are real violations of the
standard that the project charter (AGENTS.md, "Explicitly out of scope")
consciously defers — they are reported as FAIL to keep this report valid for
external auditors.

## 2. Scope — endpoint inventory

| # | Method | Path | Auth | Policy / scope | OpenAPI group | File:line |
|---|---|---|---|---|---|---|
| 1 | {{...}} | {{...}} | {{AllowAnonymous / Bearer}} | {{...}} | {{v1.public / infra / internal}} | {{...}} |

Include anonymous infra endpoints (health, OpenAPI docs, Scalar UI, `/`).

## 3. Compliance scorecard

One row per rule; one verdict column per endpoint (or a combined verdict
column when uniform). `D` = FAIL with `[charter-deferred]`.

| Rule | Rule name | {{EP1}} | {{EP2}} | {{EP3}} | {{EP4}} | Overall |
|---|---|---|---|---|---|---|
| API1:2023 | BOLA | | | | | |
| API2:2023 | Broken Authentication | | | | | |
| API3:2023 | Object Property Level AuthZ | | | | | |
| API4:2023 | Unrestricted Resource Consumption | | | | | |
| API5:2023 | Function Level AuthZ | | | | | |
| API6:2023 | Sensitive Business Flows | | | | | |
| API7:2023 | SSRF | | | | | |
| API8:2023 | Security Misconfiguration | | | | | |
| API9:2023 | Improper Inventory Management | | | | | |
| API10:2023 | Unsafe Consumption of APIs | | | | | |
| ASVS V2 | Authentication | | | | | |
| ASVS V3 | Session/token lifecycle | | | | | |
| ASVS V4 | Access control | | | | | |
| ASVS V5 | Validation & encoding | | | | | |
| ASVS V6 | Cryptography | | | | | |
| ASVS V7 | Errors & logging | | | | | |
| ASVS V8 | Data protection | | | | | |
| ASVS V14 | Configuration | | | | | |

## 4. Findings

### F-001: {{title}}

- **Rule(s)**: {{API8:2023; ASVS V14.4 (security headers)}}
- **Severity**: {{Critical/High/Medium/Low/Info}}
- **Verdict**: {{FAIL | WARN}} {{`[charter-deferred: …]` if applicable}}
- **Endpoint(s)**: {{affected endpoints}}
- **Description**: {{what is wrong and why it matters, 2–4 sentences}}
- **Evidence**: {{file:line refs with one-line quotes; probe P-xx with
  request → observed status/headers/body}}
- **Remediation**: {{concrete, minimal fix suggestion}}

(Repeat per finding. Order: FAIL first by severity, then WARN. Every non-PASS
verdict needs evidence.)

## 5. Passed checks (summary)

Brief list of the meaningful passes with their strongest evidence, e.g.
"Argon2id PHC secret hashing with dummy-hash timing equalization
(`Argon2idSecretHasher.cs:NN`, handler :NN)". No need to restate full
detection steps.

## 6. Methodology & limitations

- Static analysis of the audited commit (list top-level sources consulted:
  host `Program.cs`, module controllers/handlers/validators,
  `Directory.Packages.props`, `Directory.Build.props`, `db/migrations/`).
- Dynamic probes: `vapt_probe.py` P-01…P-12 — anonymous-access, malformed
  bearer, security/fingerprint headers, verb tampering, rate-limit
  verification, oversized body, docs exposure, CORS, correlation-id trust,
  error leakage, transport scheme. All non-destructive, no valid credentials.
- **Limitations**: safe probes only (no exploit verification, no authorized
  credential testing, no authenticated-endpoint probing); findings graded on
  code + unauthenticated responses; internal-admin deep-dive {{was / was
  not}} in scope; dependency vulnerability status {{ran
  `dotnet list package --vulnerable` / not run (offline) — see WARN on
  NU19xx suppression}}.

## Appendix: raw probe output

{{Paste or reference the probe JSON; redact nothing needed — probes contain
no credentials.}}
