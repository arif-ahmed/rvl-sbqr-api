# SBQR.Service

Secure BQR Manager — a modular monolith built on .NET 10 + ASP.NET Core. The
repo hosts a single deployable unit (`SBQR.Api`) composed of eight internal
modules (Tenancy, KeyCustody, QrGeneration, Verification, Audit, and others).
PostgreSQL stores both public-side state (`sbqr_app`) and the wrapped
private-key vault (`sbqr_key_vault`).

This README is the **onboarding guide for new developers**: a single,
ordered flow that takes you from a freshly-cloned checkout to a running
local stack with passing tests. Architecture deep-dives live in
[`docs/design/`](docs/design/) — start with `tactical-design.md`.

## 🚀 Technology Stack

| Layer | Tech |
|---|---|
| Runtime | .NET 10 + ASP.NET Core — a modular monolith, one deployable (`SBQR.Api`) |
| Data access | EF Core 10 (primary) + Dapper (escape hatch), Npgsql driver |
| Database | PostgreSQL 16 (`sbqr_app` + `sbqr_key_vault`) |
| In-process messaging | MediatR |
| Object storage | AWS S3 (real bucket — no local emulator) |
| Tests | xUnit + NetArchTest boundary rules |
| Local orchestration | Docker Compose (prod-parity) or Aspire AppHost (debug loop) |

---

## Table of contents

1. [📋 Prerequisites](#1--prerequisites)
2. [🛠️ Installation & Setup](#2--installation--setup)
3. [🐳 Bring up the local stack](#3--bring-up-the-local-stack)
4. [✅ Verify it works](#4--verify-it-works)
5. [🏗️ Where things live](#5--where-things-live)
6. [⌨️ Day-to-day commands](#6--day-to-day-commands)
7. [🔐 Secrets](#7--secrets)
8. [🗄️ Schema & seed data](#8--schema--seed-data)
9. [🧪 Run the test suite](#9--run-the-test-suite)
10. [🔧 Troubleshooting](#10--troubleshooting)
11. [📚 Where to read next](#11--where-to-read-next)

---

## 1. 📋 Prerequisites

You need the following tools installed and on your `PATH` before cloning.
Versions matter — pin to the **minimum** listed; newer is fine.

### Required Software

1. **Git 2.40+** — [Download](https://git-scm.com/downloads) · verify: `git --version`
2. **Docker Desktop** (Mac/Windows) or **Docker Engine + Compose plugin** (Linux) — Docker 24+, Compose v2 — [Desktop](https://www.docker.com/products/docker-desktop/) · [Engine](https://docs.docker.com/engine/install/) · verify: `docker --version && docker compose version`
3. **.NET SDK 10.0** — [Download](https://dotnet.microsoft.com/download/dotnet/10.0) · verify: `dotnet --version` (should start with `10.0.`)

### Development Tools (Recommended)

- **An IDE** — Visual Studio 2022 17.13+, JetBrains Rider 2025.3+, or VS Code (with C# Dev Kit)
- **Postman** — API testing against the local stack
- **A Postgres GUI (optional)** — Adminer already ships with the stack (§4); natives: DBeaver, pgAdmin, or Azure Data Studio

> **Why Docker?** Docker runs PostgreSQL locally so you don't need a system
> install of `postgres`, and the same `Dockerfile.api` used by local
> development is what gets shipped to staging and production. If Docker
> isn't an option, you can run Postgres natively and point
> `ConnectionStrings__sbqr_app` at it — see [Troubleshooting](#10--troubleshooting).

> **Why .NET SDK 10?** The repository pins `<TargetFramework>net10.0</TargetFramework>`
> in `Directory.Build.props`. Older SDKs will restore but fail at build.

### OS-specific notes

- **Windows**: enable WSL2 in Docker Desktop (Settings → Resources →
  WSL Integration). The repo runs from the WSL filesystem for fastest I/O
  when possible; Windows-native paths also work but `dotnet restore` will
  be slower.
- **macOS (Apple Silicon)**: Docker Desktop on ARM64 is fine — the official
  `postgres:16-alpine` and `mcr.microsoft.com/dotnet/{sdk,aspnet}:10.0`
  images are multi-arch.
- **Linux**: add your user to the `docker` group so you don't need `sudo`
  for every command. See <https://docs.docker.com/engine/install/linux-postinstall/>.

---

## 2. 🛠️ Installation & Setup

Run these commands from the directory where you want the project to live
(e.g. `~/source/`). Windows PowerShell equivalents are shown only where
they differ.

```bash
# 1. Clone (replace <org> with the real GitHub org when known).
git clone https://github.com/<org>/rvl-secure-bqr-manager.git
cd rvl-secure-bqr-manager

# 2. (Optional but recommended) check out develop and create your own branch.
git checkout develop
git checkout -b feature/<short-kebab-slug>

# 3. Create your local .env from the tracked template.
#    This is where you override Postgres credentials, ports, etc.
cp docker/.env.example docker/.env

# 4. (Optional, advanced) edit docker/.env if you want non-default ports.
#    The defaults work for everyone; only touch this if 8080/8081/5432 are
#    already taken on your machine.
```

Branch names follow `feature/<short-kebab-slug>` and commits follow
[conventional commits](https://www.conventionalcommits.org/)
(`feat: …`, `fix: …`, `docs: …`) — both are checked by the PR template
(see [§11](#11--where-to-read-next)).

### What the `.env` file is and isn't

| Question | Answer |
|---|---|
| Is `.env` committed to git? | **No.** `.env` and `.env.*` are in `.gitignore` at the repo root. The tracked template is `docker/.env.example`. |
| Should I share my `.env`? | **Never.** Only the `.env.example` is shareable. If a teammate needs the same override, copy from `.env.example` again. |
| Does `.env` matter for staging/production? | **No.** Only local uses it. Production injects secrets purely via environment variables (see [§ Secrets](#7--secrets)) — no `.env`, no secret manager service. |
| What goes in it? | Postgres user/password (defaults to `postgres`/`postgres` — local-only), host port mappings, and a few non-secret toggles like `KeyCustody__ActiveProvider=PlainFile`. **Never** put real DB passwords or signing keys here. |

> **Mental model:** the `.env` file is **convenience**, not **security**.
> Its presence or absence changes nothing about how the app works; it's
> only how the developer overrides defaults. Production secrets are
> environment variables — full stop. See [§ Secrets](#7--secrets) below.

---

## 3. 🐳 Bring up the local stack

Two supported ways — pick one (never both at once; each starts its own
Postgres):

- **Option A — Docker Compose** (default): prod-parity, best for "just run
  it" and CI-like verification.
- **Option B — Aspire AppHost**: debug loop with dashboard, best for
  breakpoints and fast iteration.

### Option A: Docker Compose

A single command starts the API, PostgreSQL (with both databases created),
and Adminer:

```bash
docker compose -f docker/docker-compose.yml up --build
```

What this does, step by step:

1. **`sbqr.postgres`** — pulls `postgres:16-alpine`, creates the
   `sbqr.pgdata` named volume on first run, and executes
   `docker/postgres/init/00-create-databases.sql` to create `sbqr_app` and
   `sbqr_key_vault`.
2. **`sbqr.api`** — builds from `docker/Dockerfile.api` (~2 min the first
   time, ~5s on rebuilds thanks to the BuildKit cache mount), then waits
   for Postgres to pass its healthcheck before starting. Object storage
   (`Storage:*`) points at a real AWS S3 bucket, not an emulator — see
   [`docs/dev-s3-guide.md`](docs/dev-s3-guide.md) to get bucket access and
   fill in `docker/.env`.
3. **`adminer`** — starts once Postgres is healthy.

The `up` command streams logs from all services. **Leave it
running** in one terminal; open a second terminal for the steps below.

To stop everything while keeping the Postgres data:

```bash
docker compose -f docker/docker-compose.yml down
```

To **wipe the local database and start fresh** (use this any time schema
or seed data change):

```bash
docker compose -f docker/docker-compose.yml down -v
docker compose -f docker/docker-compose.yml up --build
```

> The `-v` flag drops the named `sbqr.pgdata` volume, which is the only
> way to make the init script run again. It does not touch S3 — that's a
> real AWS bucket, not a local volume.
> See [`docker/postgres/README.md`](docker/postgres/README.md) for details.

### Option B: Aspire AppHost (debug loop)

```bash
# 1. Make sure compose is NOT running (each stack starts its own Postgres).
docker compose -f docker/docker-compose.yml down

# 2. (Optional) point the trust store at the REAL BB endpoint instead of
#    the in-repo mock; add S3 credentials / bootstrap-client hash only if
#    you need them — see [§7](#7--secrets).
dotnet user-secrets set "Parameters:trust-store-base-url" "http://..." \
  --project src/Host/SBQR.AppHost

# 3. Start everything (Postgres + SBQR.Api + BB.TrustStoreMock).
dotnet run --project src/Host/SBQR.AppHost
```

The console prints a dashboard link with a one-time token
(`http://localhost:15xxx/login?t=…`) — open it. You'll see six resources:
`sbqr-postgres`, three logical databases (`sbqr-app`, `sbqr-key-vault`,
`bb-trust-store-mock-db`), `sbqr-api`, and `bb-trust-store-mock` (the dev-only
BB trust-store stand-in — upload test keys via its
`PUT /trust-store/institutions/{id}/public-key`, browse the contract at its
`/scalar` endpoint). The API gets a
**dynamic localhost port**: click `sbqr-api` in the dashboard to find its
endpoint URL, then verify it ([§4](#4--verify-it-works)).

Apply the schema with the external migration tool (`db/migrations/*.sql`)
against the AppHost Postgres — grab its host port from the dashboard's
`sbqr-postgres` resource (same procedure as
[§8](#8--schema--seed-data), just pointed at that port). Stop with `Ctrl+C`;
data persists in a Docker volume like compose — find it via
`docker volume ls` and `docker volume rm` it for a clean database.

---

## 4. ✅ Verify it works

In a second terminal:

```bash
# Health endpoints.
curl http://localhost:8080/health/live    # → 200 {"status":"live"}
curl http://localhost:8080/health/ready   # → 200 {"status":"ready"}

# OpenAPI documents (two of them — public and internal-admin).
curl http://localhost:8080/openapi/v1.public.json          | head
curl http://localhost:8080/openapi/v1.internal-admin.json  | head

# Both project databases exist.
docker compose -f docker/docker-compose.yml exec sbqr.postgres \
    psql -U postgres -l
# Expect rows named "sbqr_app" and "sbqr_key_vault".

# Web UI for ad-hoc SQL inspection.
open http://localhost:8081    # or browse manually
#   System:   PostgreSQL
#   Server:   sbqr.postgres
#   User:     postgres
#   Password: postgres
#   Database: sbqr_app   (or sbqr_key_vault)

# Real S3 bucket is reachable with your configured credentials
# (see docs/dev-s3-guide.md §3-4 to get access and set docker/.env first).
aws s3 ls "s3://<bucket-name>/<your-vault-folder>/"
```

If all four checks pass, your local environment is fully working.

**Via AppHost instead?** Same checks, different addresses: take the
`sbqr-api` endpoint URL from the dashboard (dynamic port) and use it in
place of `http://localhost:8080` above. There is no Adminer here — inspect
the DB via the connection string on the `sbqr-postgres` resource (or the
dashboard's logs/traces). Schema still comes from `db/migrations/*.sql`
([§8](#8--schema--seed-data)).

### Port map

| What | Where |
|---|---|
| API via compose | `http://localhost:8080` |
| Adminer | `http://localhost:8081` |
| API via `dotnet run` | `http://localhost:5080` |
| API via AppHost | dynamic `http://localhost:…` — dashboard `sbqr-api` resource |
| Postgres | `localhost:5432` |
| Postgres via AppHost | dynamic `localhost:…` — dashboard `sbqr-postgres` resource |

(Override any of these in `docker/.env` — see [§10](#10--troubleshooting).)

---

## 5. 🏗️ Where things live

**How it fits together:** one deployable (`SBQR.Api`) composed of eight
`IModule` composition roots (Tenancy, KeyCustody, QrGeneration,
Verification, Audit, and others); two OpenAPI documents (public and
internal-admin); state split across `sbqr_app` (public-side) and
`sbqr_key_vault` (wrapped private keys). Authority:
[`docs/design/tactical-design.md`](docs/design/tactical-design.md).

```
rvl-secure-bqr-manager/
├── docker/                              ← Docker artefacts (compose, Dockerfile, init script)
│   ├── Dockerfile.api                   Multi-stage, non-root, prod-ready
│   ├── docker-compose.yml               Local stack: api + postgres + adminer
│   ├── .env.example                     Tracked template; cp to .env (gitignored)
│   └── postgres/
│       ├── README.md                    Init script + reset runbook
│       └── init/00-create-databases.sql Creates sbqr_app + sbqr_key_vault on first boot
│
├── docs/                                ← Architecture, design, deployment guides
│   ├── design/
│   │   ├── tactical-design.md           ★ Module layout, IModule contract, OpenAPI split
│   │   ├── ddd-strategic-design.md      ★ Aggregate boundary authority
│   │   └── database-design.md           ★ Per-module table ownership; two-DB layout
│   ├── docker/DEPLOYMENT.md             ★ Staging (ACA) + production (AKS) topology
│   ├── aspire-local-dev-guide.md        Optional Aspire AppHost inner loop (local dev only)
│   └── PERSISTENCE_DECISIONS.md         EF Core primary + Dapper escape hatch rules
│
├── src/
│   ├── SharedKernel/SBQR.SharedKernel/  Cross-cutting types only — zero project refs
│   │   └── SBQR.SharedKernel.Storage/   Reusable IObjectStorage backend (S3) — infra lives here, not in the pure kernel
│   ├── Host/SBQR.Api/                   The single deployable — Program.cs + appsettings
│   ├── Host/SBQR.AppHost/               Optional local-dev-only Aspire orchestrator (never shipped)
│   └── Modules/
│       ├── SBQR.Modules.QrCodec/        Pure library — payload + CRC + KATs
│       ├── SBQR.Modules.{Tenancy,
│       │                  InstitutionTrust,
│       │                  KeyCustody,
│       │                  QrGeneration,
│       │                  Verification,
│       │                  Audit,
│       │                  IdentityAccess}/   IModule composition roots
│
├── tests/                               xUnit test projects + NetArchTest rules
├── db/migrations/                       canonical SQL files (raw SQL; no EF) — applied by the external migration tool
├── SBQR.slnx                            Solution file (.slnx format)
├── Directory.Build.props                Shared MSBuild properties (treats warnings as errors)
├── Directory.Packages.props             Central package versions (CPM)
└── README.md                            ← you are here
```

★ = start here once you have the local stack running.

---

## 6. ⌨️ Day-to-day commands

Assuming `docker compose -f docker/docker-compose.yml up` is running in one
terminal:

| Task | Command |
|---|---|
| Run the API outside Docker (faster iteration) | `dotnet run --project src/Host/SBQR.Api/SBQR.Api.csproj` (still talks to the Postgres in Docker via the default connection string) |
| Tail API logs only | `docker compose -f docker/docker-compose.yml logs -f sbqr.api` |
| Tail Postgres logs | `docker compose -f docker/docker-compose.yml logs -f sbqr.postgres` |
| Open `psql` against `sbqr_app` | `docker compose -f docker/docker-compose.yml exec sbqr.postgres psql -U postgres -d sbqr_app` |
| Open `psql` against `sbqr_key_vault` | `docker compose -f docker/docker-compose.yml exec sbqr.postgres psql -U postgres -d sbqr_key_vault` |
| Reset the database fully | `docker compose -f docker/docker-compose.yml down -v && docker compose -f docker/docker-compose.yml up -d` |
| Stop everything (keep data) | `docker compose -f docker/docker-compose.yml down` |
| Rebuild only the API after a code change | `docker compose -f docker/docker-compose.yml up --build sbqr.api` |

### Running the API on the host (not in Docker)

If you want a tighter inner loop — edit code, hit Ctrl+F5, see the change —
run the API directly:

```bash
dotnet run --project src/Host/SBQR.Api/SBQR.Api.csproj
```

This listens on `http://localhost:5080` (from `Properties/launchSettings.json`)
and still connects to `sbqr.postgres:5432` for the database. Both projects
can be edited together; the API container only needs to be rebuilt for
changes to `Dockerfile.api` itself.

### Alternative: Aspire AppHost (optional, local dev only)

Spin-up is covered in [§3](#3--bring-up-the-local-stack) (Option B). Why
you'd choose it over compose: `SBQR.Api` runs as a plain `dotnet` process,
not a container, so:

- **Visual Studio**: set `SBQR.AppHost` as the startup project and press
  **F5** — breakpoints in `SBQR.Api` just work.
- **VS Code**: use the C# Dev Kit debugger, or run the AppHost and attach
  to the `SBQR.Api` process (guide has the `launch.json` snippet).

Dev-only defaults (JWT key, `PlainFile` custody, trust-store cadence) are
wired in; the trust store needs no setup — AppHost starts the in-repo
`BB.TrustStoreMock` and points `TrustStore__BaseUrl` at it (set
`Parameters:trust-store-base-url` only to aim at the real BB endpoint).
The two real secrets (S3 credentials, bootstrap-client hash) go in the
AppHost's own user-secrets under `Parameters:*` keys. It never ships:
`docker/Dockerfile.api` only `COPY`s its csproj so `dotnet restore
SBQR.slnx` resolves, and publishes with `/p:UseAppHost=false`.

> **Don't run compose and AppHost at the same time** — each starts its own
> Postgres. Full guide (secrets table, troubleshooting):
> [docs/aspire-local-dev-guide.md](docs/aspire-local-dev-guide.md).

---

## 7. 🔐 Secrets

The repository's stance on secrets is **zero secrets in source code**
(AGENTS.md C9 — non-negotiable launch gate). No password, connection
string, signing key, Argon2id hash, or API token is ever committed to
the repo. The contract below describes where each secret lives in each
environment.

### Where each secret lives

| Environment | Mechanism | Files touched |
|---|---|---|
| **Local dev** (`dotnet run`) | repo-root `.env` — **primary**; parsed by `Program.cs` §1 in Development only and layered last, so it beats user-secrets, env vars and `appsettings.json`. Alternative: `dotnet user-secrets` (per-developer store at `~/.microsoft/usersecrets/<id>/secrets.json` / `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`) | `.env` (gitignored; template `.env.example`) |
| **Local dev via AppHost** (`dotnet run --project src/Host/SBQR.AppHost`) | AppHost's own user-secrets store, keys under `Parameters:*` (`storage-access-key-id` / `storage-secret-access-key` / `storage-bucket-name` / `storage-service-url`, `bootstrap-client-secret-hash`, and optionally `trust-store-base-url` to bypass the in-repo mock). The trust store itself needs no setup — AppHost starts `BB.TrustStoreMock` and wires `TrustStore__BaseUrl` to it automatically. Everything else ships with the same dev-only defaults as compose, hardcoded in `AppHost.cs` | user-secrets store only — `dotnet user-secrets set "Parameters:<key>" "<value>" --project src/Host/SBQR.AppHost` (full table: `docs/aspire-local-dev-guide.md` § Local secrets) |
| **Local docker-compose** | `docker-compose.yml` injects every secret via `${VAR:-default}` env vars; `docker/.env` (gitignored, copied from `docker/.env.example`) lets you override them. The app inside the container never reads a `.env` file — compose substitutes the values itself. | `docker/.env` (gitignored) |
| **Production / on-prem** | **environment variables only**, set by the host orchestrator (systemd `EnvironmentFile=`, K8s `env:`, Render blueprint, etc.). No `.env` file in production — the dev `.env` loader never runs outside Development. | host-only; nothing repo-resident |

All three mechanisms share **one key vocabulary** — the canonical
double-underscore env-var forms (`ConnectionStrings__sbqr_app`,
`Jwt__SigningKey`, `Crypto__VaultProvider`, …); only the injection
mechanism differs per environment.

### First-time local dev setup

After a fresh clone, copy the template and fill in your personal AWS
credentials (everything else ships with working dev defaults):

```bash
cp .env.example .env
# then edit .env: set Storage__AccessKeyId / Storage__SecretAccessKey
# (and optionally your personal hashes — see below)
```

Prefer the user-secrets store instead of (or alongside) the file? Seed it
once:

```bash
# bash / WSL / macOS
./scripts/dev-seed-user-secrets.sh

# Windows PowerShell
.\scripts\dev-seed-user-secrets.ps1
```

The script seeds ten keys: both `ConnectionStrings:*`, `Jwt:SigningKey`,
`TrustStore:BaseUrl` plus the dev cadence (`SyncOnStartup` /
`SyncIntervalMinutes`), `Crypto:VaultProvider` and the shared S3 bucket
coordinates (`Storage:Region` / `BucketName` / `VaultFolder`). Each value
matches the corresponding default already declared in
`docker/docker-compose.yml` — they are **dev-infra defaults**, identical
disposable values, not real secrets. Personal AWS credentials
(`Storage:AccessKeyId` / `SecretAccessKey`) are migrated one-time from a
legacy `appsettings.Local.json` if present, otherwise set them manually
(the script prints the commands).

One additional key is **personal** and the script will not bake it
in (rotation hygiene):

```bash
# Platform bootstrap client ("client zero") — Argon2id PHC hash.
dotnet run --project src/Host/SBQR.Api/SBQR.Api.csproj -- --generate-bootstrap-secret
# Record the printed PLAINTEXT in your personal vault (1Password / KeePass / etc.),
# then put the printed PHC into user-secrets:
dotnet user-secrets set "Auth:Bootstrap:ClientSecretHash" "<phc>" \
  --project src/Host/SBQR.Api/SBQR.Api.csproj
```

Inspect / update / remove any value:

```bash
dotnet user-secrets list  --project src/Host/SBQR.Api/SBQR.Api.csproj
dotnet user-secrets set   "<key>" "<value>" --project src/Host/SBQR.Api/SBQR.Api.csproj
dotnet user-secrets remove "<key>"           --project src/Host/SBQR.Api/SBQR.Api.csproj
dotnet user-secrets clear                    --project src/Host/SBQR.Api/SBQR.Api.csproj
```

> Layering (see `Program.cs` §1): `appsettings.json` < environment
> variables < `dotnet user-secrets` < repo-root `.env` — all Development-only
> except the first two. In every other environment (`Staging`,
> `Production`, …) neither the secrets store nor the `.env` file is
> loaded — the host reads exclusively from `appsettings.json` (the single
> tracked settings file) and environment variables.
> `appsettings.{Env}.json` files are not loaded in any environment.

### Production deploys — env-var contract

`appsettings.json` declares **every** secret-shaped key with an empty
value and a `// …` comment naming the canonical env-var form. The
deployer is expected to supply each of them through the host's standard
environment-variable injection channel. The full set:

| appsettings key | Env-var form | Required |
|---|---|---|
| `ConnectionStrings:sbqr_app` | `ConnectionStrings__sbqr_app` | yes |
| `ConnectionStrings:sbqr_key_vault` | `ConnectionStrings__sbqr_key_vault` | yes |
| `Jwt:SigningKey` | `Jwt__SigningKey` | yes (32+ chars; host refuses to start otherwise in Production — `Program.cs` §10a) |
| `Jwt:Issuer` | `Jwt__Issuer` | no (default `sbqr`) |
| `Jwt:Audience` | `Jwt__Audience` | no (default `sbqr-api`) |
| `Jwt:AccessTokenTtlMinutes` | `Jwt__AccessTokenTtlMinutes` | no (default 10) |
| `Auth:Bootstrap:ClientId` | `Auth__Bootstrap__ClientId` | no (default `platform-bootstrap`) |
| `Auth:Bootstrap:ClientSecretHash` | `Auth__Bootstrap__ClientSecretHash` | yes (Argon2id PHC; never a plaintext secret) |
| `KeyCustody:ActiveProvider` | `KeyCustody__ActiveProvider` | yes (`PemVault` in Production; `PlainFile` is refused) |
| `KeyCustody:VaultKek` | `KeyCustody__VaultKek` | yes in Production (base64, exactly 32 bytes) |
| `TrustStore:BaseUrl` | `TrustStore__BaseUrl` | yes (host refuses to start if empty — `appsettings.json` `TrustStore:BaseUrl` is intentionally empty; supply the trust-store endpoint you test against via env vars, `.env`, or user-secrets) |
| `TrustStore:ApiKey` | `TrustStore__ApiKey` | reserved (BB has not published a real auth scheme yet) |
| `Storage:AccessKeyId` / `Storage:SecretAccessKey` | `Storage__AccessKeyId` / `Storage__SecretAccessKey` | no — blank falls back to the AWS SDK default credential chain; set for static-key auth |
| `Storage:BucketName` / `Storage:Region` / `Storage:VaultFolder` | `Storage__BucketName` / `Storage__Region` / `Storage__VaultFolder` | yes when `Storage:Provider` is `S3` |
| `RateLimit:TokenEndpoint:PermitLimit` | `RateLimit__TokenEndpoint__PermitLimit` | no (default 10) |
| `RateLimit:TokenEndpoint:WindowSeconds` | `RateLimit__TokenEndpoint__WindowSeconds` | no (default 60) |

> **Production bootstrap workflow** — generate the Argon2id PHC hash
> once per environment with the same CLI flag used in dev (it reads no
> host state — pure RNG, then Argon2id):
>
> ```bash
> dotnet run --project src/Host/SBQR.Api/SBQR.Api.csproj -- --generate-bootstrap-secret
> ```
>
> Pipe the PHC into the orchestrator's secret store (or directly into
> the environment file the host reads). The plaintext never persists
> past the `Console.WriteLine` in `Program.cs` §0a — store it in
> the team's password manager immediately.

### What is NEVER committed

| Path | Status | Note |
|---|---|---|
| `~/.microsoft/usersecrets/<id>/secrets.json` | outside the repo | per-developer `dotnet user-secrets` store — the ONLY local overrides channel (seed with `scripts/dev-seed-user-secrets.ps1`/`.sh`) |
| `docker/.env` | gitignored | copied from the tracked `docker/.env.example` template |
| `.env` / `.env.*` (anywhere) | gitignored | any node/python-style env files |
| `appsettings.*.local.json` / `appsettings.*.secrets.json` | gitignored | safety net only — these files are NOT loaded by the host (single-file convention: `appsettings.json` is the only settings file) |
| `**/secrets.json` | gitignored | the literal `secrets.json` filename (in case anyone ever sets one up by hand) |

### Why is `.env` dev-only?

- The repo-root `.env` is a **local-development convenience only**:
  `Program.cs` §1 parses it strictly when `ASPNETCORE_ENVIRONMENT=Development`.
  Outside Development the loader never runs — a stray `.env` next to a
  deployed binary is inert.
- A `.env` file in production would mean another file to ship, another file
  to secure at rest, another file to rotate, and another place secrets can
  leak (image layers, backups, copy-paste into chat). Injecting via the
  orchestrator's env-var channel avoids every one of those — with the exact
  same key names the `.env` already uses, so promoting a value from dev to
  prod is a copy of the right-hand side only.
- The host already loads every secret via `IConfiguration` from the same
  source regardless of how the deployer supplied it — `AddEnvironmentVariables()`
  is one of the providers it composes with the single tracked
  `appsettings.json` (`Program.cs` §1; user-secrets and the dev `.env` are
  added on top in Development only).

---

## 8. 🗄️ Schema & seed data

**The host does not own the schema and does not seed data at startup.**
This is deliberate, codified in `docs/PERSISTENCE_DECISIONS.md` §3: the
runtime DB user has no DDL privileges, and every `DbContext` in every
module explicitly **never** calls `Database.Migrate()` or
`Database.EnsureCreated()`. The host comes up silent — no DB writes,
no schema mutations, no data seeding — so an operator can apply the
canonical schema and any pre-loaded seed data independently of the
running process.

### Schema (one-time, per environment)

The canonical schema lives in `db/migrations/*.sql` and is applied by
an external migration tool / DBA, **never by the API**. Full list and
runbook: [`db/migrations/README.md`](db/migrations/README.md). Local:

```bash
for f in db/migrations/*.sql; do
  docker compose -f docker/docker-compose.yml exec -T sbqr.postgres \
    psql -U postgres -d sbqr_app -v ON_ERROR_STOP=1 -f - < "$f" || break
done
```

### Trust-directory seed (one-time, per environment)

The trust-directory data (`public.institution_registries`,
`public.institution_keys`) is **not** seeded by the host.
The previous behaviour of `DailyTrustSyncService` was to fetch from
`TrustStore:BaseUrl` and upsert on first tick. That has been changed:
the service now waits one full `TrustStore:SyncIntervalHours` period
before its first tick, so the host comes up with no DB writes.

To pre-load the trust directory out-of-band, use one of these:

| Mechanism | When to use |
|---|---|
| **`POST /v1/admin/institutions`** (the manual admin upsert endpoint, available in every environment) | One-off or small sets of institutions; the same `InstitutionUpsertService` that the sync uses is reached via MediatR. |
| **Run the sync on demand** — invoke `DailyTrustSyncService.RunOnceAsync` via a future admin endpoint or CLI flag (operator-triggered; does not change the periodic loop) | After pointing `TrustStore:BaseUrl` at a new trust store, to warm the local directory without waiting a full period. |
| **Let the periodic loop land it** | Default. First tick lands `SyncIntervalHours` after `StartAsync`; subsequent ticks follow the same cadence. |

### Verifying the host stayed silent at boot

```bash
# After `docker compose up` or `dotnet run`, watch for these two logs:
docker compose -f docker/docker-compose.yml logs sbqr.api | grep -i 'trust-sync'
#   expect: nothing — the service has not logged a tick yet
#   (the audit row "institution.trust.sync.completed" only lands after
#    the first scheduled tick, not at boot)
```

---

## 9. 🧪 Run the test suite

```bash
# Run every test project (5 assemblies, 12 tests at scaffolding time).
dotnet test SBQR.slnx -c Release

# Run just one project's tests.
dotnet test tests/SBQR.Modules.QrCodec.Tests/SBQR.Modules.QrCodec.Tests.csproj

# Architecture boundary tests (NetArchTest rules — fast, run on every PR).
dotnet test tests/SBQR.ArchitectureTests/SBQR.ArchitectureTests.csproj
```

Tests do **not** require a database — they exercise pure domain logic and
boundary invariants (e.g. QrCodec must not reference EF Core, NSec types
may only live inside `KeyCustody/Infrastructure/Custody/`). Integration
tests that touch Postgres arrive in epic-1 stories.

---

## 10. 🔧 Troubleshooting

### `docker compose` complains about `compose.yaml` vs `docker-compose.yml`

This project uses **`docker-compose.yml`** (Compose V2 syntax). If your
Docker is older than 24.x, either upgrade Docker Desktop or invoke
`docker-compose` (the legacy V1 CLI) instead — but only V2 is tested.

### Port 8080 / 5432 / 8081 already in use

Edit `docker/.env` after copying the example, and change
`SBQR_API_HOST_PORT`, `POSTGRES_HOST_PORT`, or `ADMINER_HOST_PORT` to
something free (e.g. `18080`, `15432`, `18081`). Restart the stack.

### `dotnet --version` shows something earlier than 10

Multiple SDKs can coexist; check `dotnet --list-sdks`. If the only
`10.0.x` install isn't the default, either pin via a `global.json` at the
repo root (ask the team lead) or uninstall the older versions. The build
will fail with `NETSDK1045` if a sub-10 SDK is selected by accident.

### `docker compose up` fails with "port is already allocated"

Another container (maybe Postgres from a different project) is bound to
5432. List them:

```bash
docker ps --format "table {{.Names}}\t{{.Ports}}"
```

Stop the conflicting container or change `POSTGRES_HOST_PORT` in
`docker/.env`.

### Postgres init script "didn't run" / only one database present

The init script runs **only on first boot** of an empty data directory.
If `sbqr.pgdata` already exists with the databases, the script is
ignored. Reset with `down -v`, as documented above.

### "I see SBQR.Api started but my changes aren't reflected"

You're running the API inside Docker and edited source on the host.
Rebuild and restart:

```bash
docker compose -f docker/docker-compose.yml up --build sbqr.api
```

Or run the API on the host (`dotnet run`) for faster iteration — see
[Day-to-day commands](#6--day-to-day-commands).

### Compose and AppHost fighting over Postgres

Run only one stack at a time. If `sbqr-api` talks to an unexpectedly
empty database or Postgres fails to start with a port conflict, you
probably still have the other stack running: `docker compose -f
docker/docker-compose.yml down` before starting the AppHost, and `Ctrl+C`
the AppHost before `docker compose up`.

### Linux: `docker compose` requires `sudo`

Add yourself to the `docker` group:

```bash
sudo usermod -aG docker $USER
# Log out and back in.
```

See <https://docs.docker.com/engine/install/linux-postinstall/>.

### Windows: very slow `dotnet restore` / `dotnet build`

Path translation between Windows and the Linux container WSL2 uses can
throttle I/O. Either clone the repo inside `\\wsl$\<distro>\home\<user>\`
or use a [mounted volume in WSL2 directly][wsl-perf] for the working
directory.

[wsl-perf]: https://learn.microsoft.com/windows/wsl/filesystems

---

## 11. 📚 Where to read next

Once the local stack is up, the canonical reading order is:

1. **[`docs/design/tactical-design.md`](docs/design/tactical-design.md)** —
   the architecture source of truth: module layout, `IModule` contract,
   cross-module call rules, the two OpenAPI documents.
2. **[`docs/design/database-design.md`](docs/design/database-design.md)** —
   the two-database split (`sbqr_app` + `sbqr_key_vault`) and per-module
   table ownership.
3. **[`docs/PERSISTENCE_DECISIONS.md`](docs/PERSISTENCE_DECISIONS.md)** —
   EF Core primary + Dapper escape hatch; raw-SQL migrations are
   mandatory (applied by the external migration tool), EF Core migrations
   are forbidden.
4. **[`docs/docker/DEPLOYMENT.md`](docs/docker/DEPLOYMENT.md)** — production
   topology and on-prem bring-up guide. Read this before touching CI/CD.
5. **[`docs/delivery/`](docs/delivery/)** — the epic-by-epic story plans
   for what gets built next.
6. **[`docs/bb-banglaqr-p2p-specification.md`](docs/bb-banglaqr-p2p-specification.md)** —
   the regulatory specification SBQR implements (Annex C/E conformance
   vectors live here).
7. **[`docs/dev-s3-guide.md`](docs/dev-s3-guide.md)** — local development
    against the real AWS S3 bucket: getting access, app configuration,
    CLI/GUI usage, and how the automated tests stay off the real bucket.
8. **[`.github/pull_request_template.md`](.github/pull_request_template.md)** —
    branch naming (`feature/<short-kebab-slug>`), conventional commits, and
    the PR checklist. Read before opening your first PR.
9. **[`AGENTS.md`](AGENTS.md)** — the spec-only agent charter: what the repo
    enforces (and what it deliberately doesn't). No separate coding-standards
    doc exists — follow the spec, keep methods small, prefer DI and async I/O.
