# Postman Export Scripts

Automated scripts that generate Postman collection and environment files for
the BQR Secure Manager API without needing to invoke the `/postman-export`
skill each time.

All commands below are intended to be run **from the repository root**. The
scripts resolve their own internal paths (Python source, output directory,
optional `.env` file) relative to their own location, so they behave
identically no matter where you invoke them from — but the examples assume
you're at the repo root.

## Prerequisites

- **Node.js** — only needed if the full stack isn't already running
  (used for the mock TrustStore)
- **Python 3.6+** — for the export script (stdlib only, no pip dependencies)
- **.NET SDK** — to run the API locally
- **Postman** — to import and use the generated collection

## Quick Start

### Windows (PowerShell)

```powershell
# Default — generate BOTH public and internal collections + all 4 environments
.\tools\postman-export\Generate-Postman-Export.ps1

# Generate only the public collection
.\tools\postman-export\Generate-Postman-Export.ps1 -Scope public

# Generate only the internal-admin collection
.\tools\postman-export\Generate-Postman-Export.ps1 -Scope internal

# Skip startup — API is already running elsewhere
.\tools\postman-export\Generate-Postman-Export.ps1 -Scope public -SkipStartup

# Override a single base URL on the command line
.\tools\postman-export\Generate-Postman-Export.ps1 `
  -StageBaseUrl "https://stage-api.example.com"
```

> No password prompts. The internal-admin OpenAPI document is fetched without
> HTTP Basic auth — this script is intended to be run by platform admins and
> the technical dev team against environments they already control.

### Linux / macOS (Bash)

```bash
# Default — generate BOTH public and internal collections + all 4 environments
./tools/postman-export/generate-postman-export.sh

# Generate only one scope
./tools/postman-export/generate-postman-export.sh public
./tools/postman-export/generate-postman-export.sh internal
./tools/postman-export/generate-postman-export.sh all

# Override a single base URL on the command line
STAGE_BASE_URL=https://stage-api.example.com \
  ./tools/postman-export/generate-postman-export.sh
```

## Optional: Per-developer Base URLs via `.env`

Both scripts will read `tools/postman-export/.postman.env` at startup if it
exists. The file is **gitignored** (the template
`tools/postman-export/.postman.env.example` is tracked instead). Copy the
template to get started:

```bash
cp tools/postman-export/.postman.env.example tools/postman-export/.postman.env
# edit values; leave a line out to use the built-in default
```

Supported keys:

| Key | Default |
|---|---|
| `POSTMAN_LOCAL_BASE_URL`   | `http://localhost:5001` |
| `POSTMAN_DEV_BASE_URL`     | `http://localhost:5080` |
| `POSTMAN_STAGE_BASE_URL`   | `https://stage.bqr.internal` |
| `POSTMAN_PROD_BASE_URL`    | `https://api.bqr.example.com` |

Layering, in increasing priority:

1. Built-in defaults (above)
2. `tools/postman-export/.postman.env` values
3. CLI / environment-variable overrides (`-StageBaseUrl "..."` in PowerShell,
   `STAGE_BASE_URL=... ./script.sh` in Bash)

If the `.env` file is missing the script silently falls back to the defaults —
it does not fail.

## What Each Script Does

1. **Starts mock TrustStore** on `localhost:5002` (unless already running, or
   `-SkipStartup` is passed)
   - Returns an empty institutions list so the API can start successfully
   - Stops on exit unless `-SkipCleanup` (PowerShell) / background-mode
     (Bash) keeps it running

2. **Starts the API** on `localhost:5001`
   - Waits for the API to respond on `/health`
   - The API assumes the canonical schema has been applied by the external
     migration tool — see `docs/docker/DEPLOYMENT.md` §4

3. **Fetches the OpenAPI document** from the running API
   - Public:    `http://localhost:5001/openapi/v1.public.json` (no auth)
   - Internal:  `http://localhost:5001/openapi/v1.internal-admin.json` (no auth)

4. **Runs the Python export script** (`.claude/skills/postman-export/scripts/generate_postman.py`)
   - Converts OpenAPI → Postman v2.1 collection
   - Generates four environment files: **Local, Dev, Stage, Production**
   - Outputs to `.postman/` at the repo root (gitignored)

5. **Cleans up background processes** (unless `-SkipStartup` /
   `-SkipCleanup` was passed)

When `-Scope all` (the default), steps 3–4 run **twice** — once per scope —
producing both a public and an internal collection in the same run.

## Generated Files

After a default run, `.postman/` contains:

```
.postman/
├── BQR-public.postman_collection.json         # ↓ one of these per scope
├── BQR-internal.postman_collection.json       # ↑ when -Scope "all" (default)
├── BQR-Local.postman_environment.json         # new — http://localhost:5001
├── BQR-Dev.postman_environment.json
├── BQR-Stage.postman_environment.json
└── BQR-Prod.postman_environment.json
```

(If you ran with `-Scope public` or `-Scope internal`, only the matching
collection file is produced. All four environment files are produced either
way.)

### Importing into Postman

1. **File → Import → Choose files**
   - Import each `BQR-*.postman_collection.json`
   - Then import all four `BQR-*.postman_environment.json` files

2. **Select an environment** (top-right dropdown in Postman)

3. **Fill in OAuth credentials** in the environment:
   - `clientId` — platform client ID
   - `clientSecret` — platform client secret

4. **Run `POST /v{version}/oauth/token`** to get an access token
   - The test script in that request auto-captures `access_token` into the
     environment
   - All other requests inherit it automatically as Bearer auth

## CI/CD Usage

For automated export in pipelines, run from the repo root:

```yaml
# GitHub Actions / GitLab CI / etc
- name: Generate Postman export
  working-directory: ${{ github.workspace }}
  run: |
    # PowerShell on Windows runners
    .\tools\postman-export\Generate-Postman-Export.ps1 `
      -Scope all `
      -SkipStartup

    # OR Bash on Linux/macOS runners
    ./tools/postman-export/generate-postman-export.sh all

- name: Upload artifacts
  uses: actions/upload-artifact@v3
  with:
    name: postman-export
    path: .postman/
```

Tip: in CI the API is typically not started by the script — pass
`-SkipStartup` (PowerShell) and let the runner's `startup` service bring
the API up itself.

## Troubleshooting

| Issue | Solution |
|---|---|
| "Port already in use" | Another instance is running; use `-SkipStartup` (PowerShell) or kill it manually (Bash) |
| "Python not found" | Install Python 3.6+ or ensure it's on `PATH` as `python` or `python3` |
| "Node.js not found" | Optional; only needed for the mock TrustStore. If the API is already running, use `-SkipStartup` (PowerShell) |
| "API failed to respond" | Check the temp logs: `api-output.log`, `api-error.log` |
| `POSTMAN_*_BASE_URL` not picked up | Confirm the file path is `tools/postman-export/.postman.env` (not the repo-root `.env`), no typos in key names, no surrounding quotes on values |
| Wrong base URL baked into environments | Either edit `tools/postman-export/.postman.env`, pass `-StageBaseUrl` etc. on the CLI, or set `STAGE_BASE_URL=...` in the calling shell |

## Options Reference

### PowerShell Script

```powershell
.\tools\postman-export\Generate-Postman-Export.ps1 `
  -Scope    <public | internal | all>        # default: all
  -LocalBaseUrl "http://localhost:5001"       # BQR-Local env
  -DevBaseUrl   "http://localhost:5080"       # BQR-Dev env
  -StageBaseUrl "https://stage.bqr.internal"  # BQR-Stage env
  -ProdBaseUrl  "https://api.bqr.example.com" # BQR-Prod env
  -SkipStartup                                # assume API/TrustStore already running
  -SkipCleanup                                # keep processes running after export
```

`-LocalBaseUrl`, `-DevBaseUrl`, `-StageBaseUrl`, `-ProdBaseUrl` accept an
empty string to mean "no override — use the `.env` value or built-in
default". This lets the `.env` file be the single source of truth for base
URLs.

### Bash Script

```bash
./tools/postman-export/generate-postman-export.sh [scope]
# scope: public | internal | all (default: all)
```

Environment variables override the values from `tools/postman-export/.postman.env`:

| Variable | Maps to env file |
|---|---|
| `POSTMAN_LOCAL_BASE_URL` | `BQR-Local` |
| `POSTMAN_DEV_BASE_URL`   | `BQR-Dev` |
| `POSTMAN_STAGE_BASE_URL` | `BQR-Stage` |
| `POSTMAN_PROD_BASE_URL`  | `BQR-Prod` |

```bash
# Example — point at a staging API and skip the local mock stack
POSTMAN_STAGE_BASE_URL=https://stage-api.example.com \
  ./tools/postman-export/generate-postman-export.sh public
# (no -SkipStartup equivalent in bash; the port check below detects it)
```

## Notes

- The `.postman/` output directory is **gitignored** — regenerate it when the
  API surface changes rather than hand-editing
- Both scripts automatically create the output directory if it doesn't exist
- The mock TrustStore is a minimal Node.js server; it's not suitable for
  real testing
- For Stage/Production, replace the placeholder URLs with real hostnames
  (via `tools/postman-export/.postman.env` or CLI overrides) before
  distributing collections to partners

## Related Documentation

- **Skill documentation**: `.claude/skills/postman-export/SKILL.md`
- **Python export script**: `.claude/skills/postman-export/scripts/generate_postman.py`
- **OpenAPI docs**:
  - Public:   `src/Host/SBQR.Api/openapi/v1.public.json`
  - Internal: `src/Host/SBQR.Api/openapi/v1.internal-admin.json`
- **Tracked `.env` template**: `tools/postman-export/.postman.env.example`
