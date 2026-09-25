# contracts/ — published API artifacts

## v1.public.json

The platform's **public OpenAPI contract**, published as a committed,
versioned artifact instead of existing only while `SBQR.Api` is running.
It is the single source of truth for external consumers of the FI-facing
surface (`/v1/oauth/token`, `/v1/qr/generate/static`, `/v1/qr/generate/dynamic`,
`/v1/qr/validate`, `/health/*`).

**Consumer today:** [`rvl-sbqr-fi-gateway`](https://github.com/Relief-Validation/rvl-sbqr-fi-gateway)
pins this file — it code-generates its DTOs from the artifact and runs a
CI fetch-compare (live served document vs. this pin) that fails the build
on any drift.

### Stable URL

```
https://raw.githubusercontent.com/Relief-Validation/rvl-secure-bqr-manager/develop/contracts/v1.public.json
```

This repository is **private**, so fetching requires a GitHub token with
read access to it (an unauthenticated request gets a 404):

```bash
curl -H "Authorization: Bearer $SBQR_PLATFORM_TOKEN" \
  "https://raw.githubusercontent.com/Relief-Validation/rvl-secure-bqr-manager/develop/contracts/v1.public.json"
```

Downstream CI stores such a token as a repository secret. `develop` head
always reflects the current contract; pin to a tag or commit SHA for an
immutable reference. The document itself is non-secret — the token guards
the repository, not the contract; the runtime `/openapi/v1.public.json`
endpoint remains anonymous in every environment.

### Regenerating

Whenever a `v1.public` API surface changes, regenerate **in the same PR**
as the change:

```bash
./scripts/export-openapi-contract.sh      # bash (Git Bash / Linux / macOS)
# or
./scripts/export-openapi-contract.ps1     # PowerShell
```

The script builds the host, boots it in Development on a loopback port,
captures the exact document the platform serves, then normalizes it via
`scripts/normalize-openapi-artifact.cs`, which validates the document and
strips the environment-specific `servers` block so the output is
byte-identical regardless of which machine ran the export. No database or
vault is required — nothing on this path touches them.

### Rules

- **Only `v1.public` is ever published here.** The `v1.internal-admin`
  document stays runtime-only, served to operators behind the ingress; it
  must never appear as a committed artifact.
- The artifact is captured from a real running host rather than generated
  at build time: `Microsoft.Extensions.ApiDescription.Server` emits
  OpenAPI 3.1.1, ignoring the host's deliberate `OpenApiSpecVersion.OpenApi3_0`
  pin (`SBQR.Api/Program.cs` §8) — a build-time artifact would drift from
  the served contract forever. Capture makes the pin provably equal to
  what the platform serves (modulo the stripped `servers` block).
- Version stamping: the document's `info.version` tracks the API version
  (currently `1.0.0`); the `v1.public` document-series name is pinned in
  `SBQR.Api/Program.cs` §8 alongside the internal-admin split.
