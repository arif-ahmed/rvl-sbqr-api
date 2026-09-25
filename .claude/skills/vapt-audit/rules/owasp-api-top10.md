# Rule Catalog — OWASP API Security Top 10 (2023)

Detection catalog for the SBQR API. Each rule: what it means, how to detect
it **in this codebase**, what evidence to capture, and verdict guidance.
File paths/line numbers are hints — re-verify from live code every run.
Audited surface by default: `v1.public` group + anonymous infra endpoints.

Verdicts: PASS / FAIL / WARN / N/A. Charter-deferred FAILs keep the FAIL and
get `[charter-deferred: …]` (see SKILL.md §5).

---

## API1:2023 — Broken Object Level Authorization (BOLA)

User-supplied identifiers must not grant access to other tenants' objects.

**Detection steps**
1. For each authenticated public endpoint (`/v1/qr/generate/*`,
   `/v1/qr/validate`), trace where the acting tenant comes from: expect the
   validated JWT `tenant_id` claim via
   `src/Host/SBQR.Api/Infrastructure/JwtClaimCurrentTenant.cs` — never from
   route, query, or body.
2. Check `JwtClaimCurrentTenant` behavior on a missing claim: must fail
   closed (empty tenant → downstream rejects). Verify consumers actually
   reject: e.g. `PemVaultSigningProvider`, `ValidateQrCommandHandler`
   (Grep for `Guid.Empty` / `TenantIdRequired` style guards).
3. Grep public request DTOs/validators for tenant/account identifiers the
   *caller* can supply that are later used for data scoping without a
   cross-check against the token tenant.
4. Confirm DB queries are tenant-filtered (EF `Where(t => t.TenantId == …)`)
   — no `Find(id)`-style unscoped lookups on tenant data.

**Evidence**: `file:line` of claim resolution + fail-closed guard + one
example scoped query per endpoint.

**Verdict guidance** — FAIL: any tenant object reachable from another
tenant's token (even via a guessed GUID). PASS: tenant always derived from
token, missing claim fails closed, queries scoped. N/A: `/v1/oauth/token`
(no object access).

---

## API2:2023 — Broken Authentication

**Detection steps**
1. Token endpoint hardening (`OAuthController`,
   `IssueClientCredentialsTokenCommandHandler`): client secrets stored/verified
   as Argon2id PHC hashes (not plaintext/reversible); dummy-hash verification
   on unknown client (timing equalization); uniform `401 invalid_client`
   (anti-enumeration — compare error bodies byte-for-byte across failure
   causes); `package_id` allow-list enforcement.
2. JWT validation setup (IdentityAccess module, `AddJwtBearer`): validates
   issuer, audience, signature, lifetime; `ClockSkew` explicitly bounded;
   `MapInboundClaims=false`. Confirm the scheme is registered **only** when
   `Jwt:SigningKey` is configured and ≥ 32 chars — otherwise authenticated
   routes must be unreachable, not silently anonymous.
3. Production boot guards (`Program.cs` ~line 453–492 + SharedKernel
   `DevSigningKeyGuard`): missing signing key, dev-key literal, unknown vault
   provider, `plain-file` provider must refuse to boot in Production.
4. Privilege separation: tenant credentials must not be able to mint `admin`
   scope (check scope assignment in the token handler; bootstrap admin is a
   separate credential population with its own Argon2id hash).
5. Design-level residual risks to record as **WARN** (not FAIL unless a
   concrete bypass exists): HS256 single symmetric key (signer == validator,
   rotation = restart), no `jti`/revocation list (tokens valid until expiry —
   check `AccessTokenTtlMinutes` is short), no JWKS/OIDC metadata (opaque
   partner integration).
6. Public-client exposure: FI mobile apps and (optionally) browser SPAs are
   **public clients** — a `client_secret` embedded in them is extractable
   (OAuth anti-pattern, RFC 6749 §10.1). Assess what compensates: the
   `package_id` mobile allow-list (spoofable identifier — compensating
   control, not a secret), short TTL, per-FI (not per-install) credentials
   with a rotation path, and least-privilege scopes. WARN unless a concrete
   escalation exists (e.g. an embedded credential minting `admin`).

**Evidence**: config + guard `file:line`; token failure-response comparison;
scope assignment code path.

**Verdict guidance** — FAIL: anonymous access to a protected endpoint,
plaintext secret comparison, dev signing key accepted in Production, tenant
token gaining admin scope. WARN: symmetric-JWT/no-revocation design notes.

---

## API3:2023 — Broken Object Property Level Authorization

Responses must not leak properties the caller is not entitled to; requests
must not mass-assign privileged fields.

**Detection steps**
1. Read the response DTOs of `/v1/qr/generate/*`, `/v1/qr/validate`,
   `/v1/oauth/token`: no internal ids beyond need, no key material (public
   *or* private), no hash/secret echo, no other-tenant metadata.
2. `GET`-style leakage: none of the public endpoints return lists, but check
   anything that returns an entity (validation history?) for over-broad
   serialization (AutoMapper profiles — audit mapping profiles for private
   members).
3. Mass assignment: confirm strict JSON (`JsonUnmappedMemberHandling.Disallow`,
   `Program.cs`) still set; check request DTOs for fields that could escalate
   (e.g. caller-supplied `scopes`, `tenant_id`, `institution_id` on token
   request → must be ignored/rejected server-side, scopes derived from
   server-side credentials only).

**Verdict guidance** — FAIL: response contains private key material, another
tenant's data, or caller-supplied privilege field honored. PASS: DTOs minimal,
unmapped members rejected.

---

## API4:2023 — Unrestricted Resource Consumption

**Detection steps**
1. Rate limiting: locate the ASP.NET `RateLimiter` policy in `Program.cs`.
   Enumerate which endpoints carry `[EnableRateLimiting]`. Any authenticated
   public endpoint without a limiter is a finding (QR generate/validate
   historically unthrottled — verify current state).
2. Request size: check `[RequestSizeLimit]`, `MaxRequestBodySize`,
   `MaxRequestFormSize` — none historically set → Kestrel default (~28.6 MiB)
   applies. The token endpoint's form read is bounded (4 KiB) by
   `RateLimitClientIdExtractor` — confirm.
3. Payload complexity: validators cap field lengths (`RecipientQrFieldsValidator`,
   `ClientId ≤ 100`, `ClientSecret ≤ 512`); TLV/CRC parse of attacker-controlled
   QR strings must be length-bounded (check the verification parser for
   loops without upper bounds).
4. Costly flows: Ed25519 sign per generate, Argon2id per token attempt
   (that one is rate-limited + dummy-hash equalized). Unthrottled signing
   = CPU exhaustion surface.

**Verdict guidance** — FAIL: authenticated public endpoints without any rate
limit `[charter-deferred: rate-limit breadth — AGENTS.md "Explicitly out of
scope"]` unless the charter changed. FAIL (no annotation): unbounded parsing
loop on attacker input. PASS: all public endpoints bounded (then drop the
annotation naturally).

---

## API5:2023 — Broken Function Level Authorization

**Detection steps**
1. Every public controller action must carry `[Authorize(Policy = …)]` with
   the correct policy (`qr:generate` / `qr:validate` / token = `[AllowAnonymous]`
   by design). Grep for `[AllowAnonymous]` outside the token endpoint and
   health/docs — each hit is FAIL unless deliberately public.
2. Internal controllers (`GroupName = "internal"`) must require
   admin/key-admin policies — even though they're outside default scope,
   *their protection state* is checkable statically; note as informational
   cross-reference, deep-dive only in internal-admin scope runs.
3. Anonymous infra endpoints in `Program.cs` (~line 490–580): `/health/*`,
   `/openapi/v1.public.json`, **`/openapi/v1.internal-admin.json`**, `/docs/*`,
   `/` redirect, dev-only `/admin/_routes` (must be Development-gated —
   verify the env check).

**Verdict guidance** — FAIL: protected function reachable anonymously
(including the internal-admin OpenAPI document/Scalar UI exposed anonymously
in all environments — that's usually filed under API8, pick one home and
cross-reference). FAIL: `/admin/_routes` reachable outside Development.

---

## API6:2023 — Unrestricted Access to Sensitive Business Flows

**Detection steps**
1. Enumerate the sensitive flows: token issuance, QR generation, QR
   validation (trust-key oracle), tenant lifecycle (internal).
2. For each public flow: what stops automation at scale? Token: rate limit.
   QR generate: nothing historically — assess (bulk generation for
   enumeration/abuse). QR validate: replay window + unique index exist for
   *duplicate* validation, but is *probing* many different QR payloads
   unthrottled? (ties to API4 — record once here with cross-ref).
3. Check validation responses don't form a trust-key enumeration oracle
   (distinct errors for unknown institution vs bad signature would let an
   attacker map the trust store — compare error codes emitted by
   `ValidateQrCommandHandler` for key-not-found vs signature-invalid).

**Verdict guidance** — FAIL: automated bulk abuse with zero friction on a
sensitive flow (annotate charter-deferred where applicable). WARN: oracle-
grade distinguishable errors without scale abuse. PASS: flow bounded.

---

## API7:2023 — Server-Side Request Forgery (SSRF)

**Detection steps**
1. Grep for all outbound HTTP the server performs: `HttpClient`,
   `HttpTrustStoreClient`, `IHttpClientFactory` registrations. Target URLs
   must come from trusted config (`TrustStore:BaseUrl`), never from request
   input.
2. Grep request DTOs/validators for URL/URI/hostname fields the caller
   controls that later reach an outbound request, redirect, or file load.
3. Check redirect handling on `HttpTrustStoreClient` (auto-redirect to
   file:// or internal hosts historically not a .NET risk for file://, but
   record redirects policy if configurable).

**Verdict guidance** — FAIL: any caller-influenced outbound URL. PASS: all
outbound targets config-pinned (record the config key). Note plain-HTTP
unauthenticated trust-store channel under API10.

---

## API8:2023 — Security Misconfiguration

**Detection steps**
1. Security headers: Grep the whole host for `X-Content-Type-Options`,
   `Strict-Transport-Security`, `Content-Security-Policy`, `X-Frame-Options`
   (custom header middleware / `app.Use(...)`). Historically none — confirm,
   and confirm via probe (P-03).
2. TLS: `UseHttpsRedirection` / `UseHsts` absent; binding plain HTTP
   (`launchSettings.json`, docker-compose `http://+:8080`). In-app TLS absent
   is `[charter-deferred: TLS termination at host]` — but the *report* still
   grades the endpoint FAIL when served over plain HTTP with no HSTS.
3. CORS: declared clients are FI mobile apps and **optionally browser apps**.
   - No `AddCors`/`UseCors` → same-origin default = browsers blocked:
     **PASS-with-note while browser support is optional** (deny-by-default is
     safe); the moment a browser client ships, absence becomes **FAIL**
     (misconfigured for its declared client population).
   - If CORS *is* configured: explicit FI-domain allow-list only. **FAIL** on
     `AllowAnyOrigin`, origin reflection, or unneeded
     `AllowAnyHeader`/`AllowAnyMethod`. No `Access-Control-Allow-Credentials`
     should exist (Bearer tokens, not cookies). Probe P-09 sends two hostile
     origins — both must not be reflected.
4. Verbose errors: no global exception handler / `AddProblemDetails`
   historically — unhandled exceptions rely on ASP.NET defaults; verify dev
   exception page (`UseDeveloperExceptionPage`) is Development-only. Probe
   P-12 checks 4xx bodies for stack traces.
5. Inventory/debug surface: anonymous `/openapi/v1.internal-admin.json` +
   `/docs/internal-admin` (usually the headline finding here or under API5 —
   pick one home, cross-ref the other); `/admin/_routes` Development-only.
6. Dependency hygiene: `Directory.Build.props` `NoWarn` suppresses
   NU1902/NU1903/NU1904 (advisory noise suppression) — WARN with evidence;
   run `dotnet list package --vulnerable` if network allows, else cite the
   suppression and pinned overrides (`Microsoft.OpenApi 2.4.0`,
   `System.Drawing.Common` pin) in `Directory.Packages.props`.
7. Default credentials: `postgres/postgres`, dev JWT key literal,
   `sbqr-dev-vault-kek-v1` KEK — all dev-only; verify Production guards
   still cover each (boot guards + compose is dev-only).
8. Browser-client hardening (browsers are optional declared clients):
   - Auth must be Bearer-header only — Grep for cookie authentication
     (`AddCookie`, cookie auth scheme). Any cookie/ambient-credential auth
     on these endpoints = CSRF surface → **FAIL**; Bearer-only → record
     CSRF as **N/A** with that rationale.
   - CSP / `X-Content-Type-Options` (step 1) are the response-side XSS
     mitigations a browser consumer relies on — grade them FAIL if absent
     once browser clients ship.
   - Token *storage* (memory vs localStorage) is a client-side concern the
     server cannot verify — record as INFO recommendation (prefer memory,
     never localStorage for long-lived tokens).

**Verdict guidance** — FAIL: missing security-header middleware; internal
docs anonymous; dev debug surface in non-dev env. WARN: advisory suppression,
residual dev literals guarded by boot gates.

---

## API9:2023 — Improper Inventory Management

**Detection steps**
1. Inventory the inventory: two OpenAPI groups (`v1.public`,
   `v1.internal-admin`) + minimal APIs. Any route *not* in either doc?
   Grep `[ExcludeFromDescription]` and map minimal-API routes — each is
   shadow surface (health is fine; `/admin/_routes` must be dev-gated).
2. Versioning: `Asp.Versioning` — are older API versions still routed when
   `v2` appears? Check `AssumeDefaultVersionWhenUnspecified` and any
   deprecated-version policy.
3. OpenAPI doc accuracy: any endpoint missing `Produces`/error responses so
   consumers mis-learn the contract? (spot-check, INFO).

**Verdict guidance** — FAIL: live route absent from both docs and not
health-classified; unsupported version still serving. PASS: split docs
accurate, shadow routes dev-only.

---

## API10:2023 — Unsafe Consumption of APIs

The API consuming third-party/interdependent services must treat them as
untrusted.

**Detection steps**
1. Trust-store channel: `HttpTrustStoreClient` fetches institution public
   keys from `TrustStore:BaseUrl` — historically **plain HTTP, no auth, no
   signature pinning** (`http://bb-trust-store-mock:8080`). A MITM feeding
   attacker keys breaks the entire verification guarantee → typically FAIL
   (or WARN if production channel is contractually defined elsewhere — check
   `docs/environments.md` / spec; if undefined, FAIL).
2. Consumed QR payloads (attacker-controlled): TLV parser must bound
   lengths/depth; `Ed25519SignatureVerifier` returns false (not throw) on
   malformed keys; CRC/TLV parse failures mapped to uniform errors. Check
   for regex/backtracking or unbounded loops on input.
3. Retry logic: `Polly` usage — retries on the trust channel must not
   amplify (bounded attempts, no infinite).
4. Interceptor output consumption: does validation trust sync-cached keys
   without re-validation at use time? (cache poisoning window).

**Verdict guidance** — FAIL: trust keys fetched over unauthenticated
unencrypted channel with no integrity check (annotate charter status if the
BB production channel contract genuinely defines security elsewhere — then
WARN with the reference). PASS: bounded parsing + verified channel.
