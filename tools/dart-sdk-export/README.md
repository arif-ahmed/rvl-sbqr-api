# Dart SDK Generator

Generates the Dart/Flutter client SDK for BQR Secure Manager's `v1.public`
API (`sdks/dart/bqr_public_client/`) from the live OpenAPI document. Mirrors
`tools/postman-export/` — same start-services-and-fetch pattern, different
output.

The generated SDK is documented for **consumers** (mobile team) in
`sdks/dart/README.md`. This file is for whoever regenerates it.

## Prerequisites

- **Node.js** — for the mock TrustStore and the OpenAPI patch step
- **.NET SDK** — to run the API locally
- **Docker Desktop** — runs `openapi-generator-cli` in a container; no local
  Java or Dart SDK install required

## Quick start

### Windows (PowerShell)

```powershell
.\tools\dart-sdk-export\Generate-Dart-Sdk.ps1

# API already running elsewhere on :5001
.\tools\dart-sdk-export\Generate-Dart-Sdk.ps1 -SkipStartup
```

### Linux / macOS (Bash)

```bash
./tools/dart-sdk-export/generate-dart-sdk.sh

# API already running elsewhere on :5001
./tools/dart-sdk-export/generate-dart-sdk.sh --skip-startup
```

## What it does

1. Starts the mock TrustStore (`:5002`) and the API (`:5001`), same as the
   Postman export scripts (skipped with `-SkipStartup` / `--skip-startup`).
2. Fetches `GET /openapi/v1.public.json` → `.openapi/v1.public.json`
   (gitignored — ephemeral).
3. Runs `patch-openapi-security.js` to produce `.openapi/v1.public.patched.json`.
   This step exists because the raw document has two gaps that only matter
   for *client-generation*, not for the real API:
   - No `securitySchemes` entry — `Microsoft.AspNetCore.OpenApi`'s native
     generator doesn't emit one from `[Authorize(Policy = ...)]` alone
     (verified against a running instance: `components.securitySchemes` is
     empty). Without patching, the generated client would have no
     Authorization-header plumbing at all.
   - No `requestBody` on `POST /v1/oauth/token` — `OAuthController` reads the
     body manually (`Request.ReadFormAsync` / `ReadFromJsonAsync<TokenRequest>`)
     instead of a typed action parameter, so ApiExplorer never sees a shape
     to describe. Without patching, the generated `v1OauthTokenPost()` takes
     zero arguments and can't actually send credentials.

   The patch mirrors `SBQR.Modules.IdentityAccess.Api.Contracts.TokenRequest`
   field-for-field. It never touches the real API contract or `Program.cs`
   — only the throwaway spec copy used for generation.
4. Runs `openapi-generator-cli` (Docker image `openapitools/openapi-generator-cli`)
   with the `dart-dio` generator, output to `sdks/dart/bqr_public_client/`.
5. Stops the background processes (unless `-SkipStartup`/`--skip-startup`
   was passed).

## Generator choice

`dart-dio`, not the plain `dart` generator — it produces a `dio`-based client
with a proper `AuthInterceptor` pipeline (`setBearerAuth`, `setOAuthToken`,
etc.), which is what lets `sdks/dart/README.md`'s Quick Start work with one
`setBearerAuth` call instead of manually attaching headers per-request.

## When to regenerate

- The `v1.public` OpenAPI surface changed (new endpoint, new field, new
  response shape) — regenerate and diff `sdks/dart/bqr_public_client/`
  before committing.
- **Scope**: this only covers `v1.public`. `v1.internal-admin` is
  platform-team-only and has no SDK — use the Postman collection
  (`tools/postman-export/`) for that surface instead.

## Output

```
sdks/dart/
├── README.md                    # mobile-team-facing usage docs
└── bqr_public_client/           # the generated Dart package (committed)
```

`sdks/dart/bqr_public_client/` is **committed**, unlike `.postman/` /
`.openapi/` — the mobile team consumes it directly via a path or git
dependency (see `sdks/dart/README.md`), so it can't be gitignored.

## Troubleshooting

| Issue | Solution |
|---|---|
| `docker: command not found` / Docker Desktop not running | Start Docker Desktop; the script needs it for `openapi-generator-cli` |
| Generated output has `type: any` model fields or the mount path shows a `C:\Program Files\Git\...` prefix on Windows | MSYS/Git-Bash path mangling — the scripts already set `MSYS_NO_PATHCONV=1` around the `docker run`; don't strip that if you're editing them |
| "Port already in use" | Another instance is running; pass `-SkipStartup` / `--skip-startup` or stop it |
| `v1OauthTokenPost` takes no arguments after a regeneration | The patch step didn't run or was skipped — the requestBody patch is what adds `grantType`/`clientId`/`clientSecret`/`packageId` parameters |

## Related

- **Generated SDK + mobile-team docs**: `sdks/dart/README.md`
- **OpenAPI patch script**: `tools/dart-sdk-export/patch-openapi-security.js`
- **Live OpenAPI document**: `http://localhost:5001/openapi/v1.public.json` (dev)
- **Postman collections** (covers both `v1.public` and `v1.internal-admin`): `tools/postman-export/`
