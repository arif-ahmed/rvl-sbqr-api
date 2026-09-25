---
name: postman-export
description: Use when the user asks to generate, export, or update a Postman collection for the BQR Secure Manager API, wants Postman environments for dev/stage/production, or wants a way to test the public or internal-admin endpoints with bearer-token auth chaining.
---

# BQR Postman Export

Generates a Postman v2.1 collection (public **or** internal-admin surface, never both
in one file) plus three environment files (Dev, Stage, Production) with bearer-token
auto-capture, from this repo's live OpenAPI documents.

## Why scope matters (read before running)

This API publishes **two separate OpenAPI documents**, already split server-side:

- `v1.public` — anonymous-readable, only non-internal endpoints (QR generation/verification, OAuth token).
- `v1.internal-admin` — HTTP Basic-auth gated, only `[ApiExplorerSettings(GroupName = "internal")]`
  endpoints (tenant/institution/credential/crypto-key admin surfaces).

A controller is internal **iff** it carries `[ApiExplorerSettings(GroupName = "internal")]` —
do not infer scope from the `/admin/` URL segment; `CryptoKeysController` is internal but has
no `/admin/` segment. Trust the OpenAPI document split, not route text.

**Always generate exactly one scope per run.** Never merge both docs into a single
collection — the internal-admin surface (tenant credentials, crypto keys, institution
trust) must not leak into a collection meant for public/partner distribution.

## Workflow

1. **Ask which scope to generate** if the user didn't already say (use AskUserQuestion):
   - "Public API" — safe to share with external partners/QA.
   - "Internal Admin API" — internal engineering/ops only, requires Basic-auth credentials to fetch its OpenAPI doc.
   Do not default silently to one or the other — confirm every time.

2. **Make sure the app is running locally** (needed to fetch the live OpenAPI JSON):
   ```
   dotnet run --project src/Host/SBQR.Api
   ```
   Default: `http://localhost:5080`.

3. **Fetch the matching OpenAPI document and generate**:

   Public:
   ```
   python .claude/skills/postman-export/scripts/generate_postman.py \
     --scope public \
     --openapi http://localhost:5080/openapi/v1.public.json \
     --out-dir .postman
   ```

   Internal (anonymous — the Basic-auth docs gate was removed):
   ```
   python .claude/skills/postman-export/scripts/generate_postman.py \
     --scope internal \
     --openapi http://localhost:5080/openapi/v1.internal-admin.json \
     --out-dir .postman
   ```

4. **Report what was generated**: request count, folder names, and remind the user
   the `.postman/` output is gitignored scratch — re-run this skill any time the API
   surface changes rather than hand-editing the JSON.

5. Optionally pass `--dev-base-url` / `--stage-base-url` / `--prod-base-url` if the
   user gives real Stage/Production hostnames (the script's defaults are placeholders —
   `stage.bqr.internal` / `api.bqr.example.com` — that must be corrected before real use).

## How auth chaining works

- The collection sets **collection-level auth** to Bearer `{{accessToken}}`, so every
  request inherits it automatically — no per-request setup.
- The `POST /v{version}/oauth/token` request is the one exception: it's marked
  `noauth` (it's `[AllowAnonymous]` and carries `client_id`/`client_secret` in its own
  body) and carries a **test script** that reads the JSON response and runs
  `pm.environment.set('accessToken', body.access_token)`.
- This request is **injected into every generated collection** (public AND internal).
  The public OpenAPI doc already exposes the endpoint; for the internal-admin doc it
  is synthesised by the generator so internal users always have the same auth entry
  point before invoking any admin operation. No duplication: if the doc already
  declares the endpoint, the synthesised one is skipped.
- Practical effect: run the token request once per environment, then every other
  request in that environment picks up the fresh token automatically. Re-run the
  token request whenever it expires (`expires_in` in the response).
- `clientId` / `clientSecret` are environment variables (type `secret`, empty by
  default) — the user fills them in per environment; never hardcode credentials into
  the collection JSON.

## Environments

Four environment files are always produced: `BQR-Local`, `BQR-Dev`, `BQR-Stage`, `BQR-Production`.
Each defines `baseUrl`, `clientId` (secret), `clientSecret` (secret), `accessToken`
(secret, populated automatically by the token request's test script). Import all
four into Postman and switch the active environment per target — no collection
changes needed to move between local/dev/stage/production.

The `BQR-Local` environment targets the developer's locally-running API
(default `http://localhost:5001`); use it when validating against an instance
started by `dotnet run` or the postman-export script itself.

## Common mistakes

| Mistake | Fix |
|---|---|
| Generating without asking scope first | Always confirm public vs internal via AskUserQuestion before running anything |
| Merging public + internal into one collection | Run the script once per scope, produce two separate collection files if both are needed |
| Assuming `/admin/` marks internal endpoints | Trust the OpenAPI doc split (`GroupName = "internal"`), not URL text |
| Hardcoding a bearer token into the collection | Token must come from the `oauth/token` request's test script into `{{accessToken}}` |
| Leaving Stage/Production `baseUrl` as the script's placeholder | Ask the user for real hostnames and pass `--stage-base-url`/`--prod-base-url` |
| Committing generated `.postman/` output or real credentials to git | Treat `.postman/` as local scratch; keep it out of version control |
