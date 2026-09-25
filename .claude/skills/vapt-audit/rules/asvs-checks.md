# Rule Catalog — OWASP ASVS 4.0.3 (selected chapters)

Chapter-level checks selected for a machine-to-machine REST API with
JWT client-credentials auth. Citations use section granularity (e.g.
"ASVS V6.2") to stay stable; paraphrase the requirement in the finding so
the report is readable without the ASVS document. Same verdict rules as the
API Top 10 catalog. Re-verify from live code every run.

---

## V2 — Authentication

### V2.2 / V2.4 — Credential storage & verification
- Client secrets must be stored only as salted adaptive hashes: verify
  Argon2id PHC strings (Isopoh) with OWASP-2024-ish parameters (m=64 MiB,
  t=3, p=1) in `Argon2idSecretHasher`; verify never throws on malformed
  input. Check both credential populations: bootstrap admin
  (`Auth:Bootstrap:ClientSecretHash`) and tenant FI credentials
  (`tenant_configurations`).
- FAIL: plaintext/reversible secret anywhere reachable at runtime (dev
  seed scripts with literals = WARN if Production-guarded, FAIL if a
  production path reads them).
- Verify: unknown-client path performs a dummy hash verification
  (timing equalization).

### V2.2.1 / V2.5.4 — Anti-automation on credential endpoints
- Token endpoint must be rate-limited (`[EnableRateLimiting(TokenEndpoint)]`)
  with a partition that cannot be trivially bypassed (key includes client_id
  AND remote IP; body read bounded 4 KiB; client_id charset-bounded).
- FAIL if limiter removed or partition key weakened.

### V2.1.1 / V2.8 — Machine credential hygiene
- Secrets never in source for production paths: check `appsettings*.json`,
  compose files, scripts — dev literals must be boot-guard-blocked in
  Production (cross-ref API2 step 3).

### V2.2 — Public clients (FI mobile apps / optional browser SPAs)
- Client-credentials from a public client means the secret ships inside the
  app (extractable). Evaluate compensating controls: `package_id` mobile
  allow-list (spoofable — control, not secret), short token TTL, per-FI
  credential rotation, least-privilege scopes (tenant can never mint
  `admin`). **WARN** as a design note; **FAIL** only if an embedded
  credential can escalate (cross-ref API2 step 6).

---

## V3 — Session Management (token lifecycle)

### V3.2 — Token lifetime
- Access-token TTL must be short (check `Jwt:AccessTokenTtlMinutes`,
  historically 10 min — sensible for M2M). FAIL if TTL > 60 min or
  unbounded. Record the value.

### V3.3 — Revocation & renewal
- No `jti`, no revocation/deny-list, no refresh flow (client-credentials
  re-issues instead): WARN — lost-secret window = TTL; acceptable for
  short-lived M2M tokens, state the residual risk.
- INFO for browser clients: token storage is client-side — recommend memory
  (or sessionStorage at most), never localStorage for long-lived tokens;
  server cannot verify this, record as recommendation only.

### V3.5 — Token binding/validation strictness
- Validator must enforce issuer, audience, signature algorithm allow-list
  (HS256 only — reject `alg:none` / algorithm confusion by construction of
  `TokenValidationParameters`), lifetime with bounded `ClockSkew`
  (historically 1 min). FAIL: missing issuer/audience validation or
  unbounded skew.
- Confirm `MapInboundClaims=false` (no legacy claim-type confusion).

---

## V4 — Access Control

### V4.2 — Per-operation enforcement
- Every endpoint declares an authorization policy (or deliberate
  `[AllowAnonymous]`); no "protected by convention" (unmarked = anonymous
  route reachable by accident is FAIL). Cross-ref API5 detection steps —
  run them, file once, cross-reference.

### V4.3 — Fail-closed defaults
- Missing/invalid tenant claim → request must fail, not default to a
  wildcard tenant. Trace `JwtClaimCurrentTenant` consumers for fail-closed
  guards.

### V4.5 / V14.6 — Tenant isolation strength
- Isolation is application-level EF filtering only; PostgreSQL RLS
  explicitly deferred (`db/migrations/README.md` ~line 88). WARN with
  `[charter-deferred: RLS post-GA]` — one missing `Where(TenantId==)` in a
  new query silently crosses tenants; note as accepted risk with the
  compensating control (code review + tests).

---

## V5 — Validation, Sanitization & Encoding

### V5.4 — Input binding strictness
- Strict JSON (`JsonUnmappedMemberHandling.Disallow`) must remain on; every
  request DTO validated by FluentValidation via the MediatR
  `ValidationBehavior` pipeline (auto-registration from module assemblies).
  Spot-check: pick each public endpoint's validator, confirm length caps
  exist (`ClientId ≤ 100`, `ClientSecret ≤ 512`, QR field byte caps).
- FAIL: any public endpoint accepting unbounded-length attacker strings
  that reach parsing/signing loops.

### V5.5 — Payload size limits
- Body-size limits: only the token-endpoint form read is bounded; Kestrel
  default (~28.6 MiB) applies elsewhere. File under API4 cross-ref; verdict
  follows API4.

---

## V6 — Cryptography

### V6.2.1 — Secret hashing
- Argon2id with documented work factors (see V2.4). PASS requires
  parameters visible in code and PHC-format storage.

### V6.2.2 / V6.2.3 — Algorithms & keys
- QR signing: Ed25519 via NSec/BouncyCastle, keys held as AES-256-GCM
  encrypted blobs (nonce + tag + handle-as-AAD, atomic writes) with KEK from
  `KeyCustody:VaultKek` — verify provider switch guard (unknown provider =
  no boot) and that the dev fallback KEK (`SHA256("sbqr-dev-vault-kek-v1")`)
  is Production-blocked.
- CSPRNG for key generation (BCL `RandomNumberGenerator`), not `Random`.
- JWT: HS256 symmetric — WARN (signer==validator acceptable for a
  self-issued M2M IdP, but key rotation and algorithm agility are limited;
  cross-ref API2 step 5). FAIL only if a weaker-than-HS256 algorithm or a
  key < 256 bits is accepted.

---

## V7 — Error Handling & Logging

### V7.1/7.3 — No stack traces, no secret leakage
- No global exception handler / ProblemDetails registration historically —
  WARN (unhandled exceptions fall to ASP.NET defaults; verify
  `UseDeveloperExceptionPage` is Development-only). Probe P-12 checks
  response bodies.
- Grep log statements (`[LoggerMessage]`, `LogError`) for secret-bearing
  interpolations (client_secret, token, key PEM, KEK). Audit log rows record
  `client_id`, scopes, `package_id` — confirm these are treated as public
  identifiers, not secrets.
- `--generate-bootstrap-secret` CLI printing the plaintext once to console:
  INFO/WARN (by-design bootstrap, document it).

### V7.7/7.9 — Audit trail
- `audit_logs` with SHA-256 hash chain + advisory locks (`AuditLogger`) —
  PASS if chain verification exists and failures are surfaced.

---

## V8 — Data Protection

### V8.1 / V8.2 — At rest & in transit
- Private keys at rest: AES-256-GCM encrypted stores (Local + S3 variants) —
  verify plaintext key never hits disk/log (atomic write to temp + rename).
- In transit: API plain HTTP in-app `[charter-deferred: TLS at host]`;
  trust-store channel plain HTTP (cross-ref API10). Grade FAIL for any
  endpoint served without transport protection at its effective edge;
  annotate deferral where the reverse proxy is the declared control.

### V8.3 — Sensitive data exposure in responses
- Token response must contain only `access_token`, `token_type`,
  `expires_in`, `scope` (no issued credential echo). QR generate response:
  QR payload + metadata only, no key handles. Cross-ref API3.

---

## V14 — Configuration

### V14.4 — HTTP security headers & HSTS
- No header middleware historically → FAIL (medium severity, cross-ref
  API8). HSTS absent → FAIL `[charter-deferred: TLS at host]`.
- CORS with browser clients declared: absent policy = deny-by-default →
  PASS-with-note *while browser support is optional*; once a browser client
  ships, an explicit FI-origin allow-list is required — wildcard or origin
  reflection = FAIL (cross-ref API8 step 3, probe P-09).
- CSRF: Bearer-header-only auth (no cookies) → N/A with rationale; cookie
  auth anywhere on public endpoints = FAIL (cross-ref API8 step 8).

### V14.5 — Debug & admin surface
- `/admin/_routes` Development-only (verify env gate). Scalar UI + internal
  OpenAPI doc anonymous in all environments → FAIL (info disclosure).

### V14.7 / dependency hygiene
- Central Package Management with pinned overrides; `NoWarn` suppresses
  NU1902/03/04 in `Directory.Build.props` (~line 76) → WARN: advisory
  signals muted at build time. If network is available, run
  `dotnet list package --vulnerable --include-transitive` and record output
  (timeout 60s, non-blocking).

### V14.8 / V14.2 — Startup hardening
- Production boot guards (missing signing key, dev key literal, unknown
  vault provider, plain-file provider) — verify each still refuses boot.
  PASS requires all four guards demonstrably present.
