# Aspire AppHost — local dev guide

`src/Host/SBQR.AppHost` is an **optional, local-development-only** way to run
this repo's stack, aimed at a nicer `dotnet run`/debugging inner loop than
`docker compose up`. This doc assumes you've never used .NET Aspire before.

If you just want to run the app, `docker compose -f docker/docker-compose.yml
up --build` (see the root [README.md](../README.md)) still works exactly as
before and remains the source of truth for CI and prod-parity testing. The
AppHost is additive — nothing here changes how the app ships.

## What Aspire is (and isn't, here)

.NET Aspire is an **orchestrator + dashboard** for running several local
projects/containers together during development: one command starts
everything, wires up connection strings and URLs between them, and gives you
a single web UI with live logs, traces, and environment variables per
resource — instead of juggling multiple terminals or `docker compose logs`.

What it is **not**, in this repo: it is not an observability/telemetry layer
for the shipped app. Aspire's other half — `ServiceDefaults` (OpenTelemetry,
built-in health-check wiring, service discovery baked into the API itself) —
is deliberately **not used here**. `AGENTS.md` is a spec-only charter:
`docs/bb-banglaqr-p2p-specification.md` doesn't call for OpenTelemetry or
similar controls, and AGENTS.md is explicit that such things should only be
added "via a separate approved doc," not as a side effect of adopting a dev
tool. `SBQR.Api` keeps its existing hand-rolled
`/health/live` / `/health/ready` endpoints, completely unchanged — the
AppHost just happens to poll `/health/live` from the outside to know when
`sbqr-api` is ready.

## AppHost vs. docker-compose — when to use which

| | `docker-compose.yml` | `SBQR.AppHost` |
|---|---|---|
| Used for | CI, prod-parity testing, "just run it" | Local debugging inner loop |
| Runs `SBQR.Api` as | A container (rebuild to see code changes) | A normal `dotnet` process (edit + rerun, or attach a debugger) |
| Dashboard/log UI | `docker compose logs -f`, Adminer for the DB | Aspire dashboard (logs, traces, env vars, all resources in one page) |
| Touches `Dockerfile.api` | Yes, it's the point | No, never |

**Don't run both at once.** Both stacks try to start a Postgres container;
docker-compose binds host port `5432` by default, while Aspire's Postgres
resource typically picks its own (usually different) port, so a collision is
unlikely but not guaranteed — and running the same logical app twice against
two different databases is confusing regardless. Stop one
(`docker compose -f docker/docker-compose.yml down`) before starting the
other.

## Running it

```bash
dotnet run --project src/Host/SBQR.AppHost
```

The console prints a dashboard link with a one-time login token, e.g.:

```
Dashboard: http://localhost:15250/login?t=<token>
```

Open it in a browser (it should also auto-launch). You'll see six resources:
`sbqr-postgres` (the Postgres container), three logical databases
(`sbqr-app`, `sbqr-key-vault`, `bb-trust-store-mock-db` — the actual Postgres
database names underneath are `sbqr_app`, `sbqr_key_vault`,
`bb_trust_store_mock`), plus two projects: `sbqr-api` and
`bb-trust-store-mock` (the dev-only BB trust-store stand-in). Click any
resource to see its logs, environment variables, and (for the projects)
console output live. The mock's contract docs live at its `/scalar`
endpoint; upload test keys via its
`PUT /trust-store/institutions/{id}/public-key`
(see `src/Host/BB.TrustStoreMock/README.md`).

`TrustStore__BaseUrl` is REQUIRED — the host refuses to boot without it.
The AppHost points it at the in-repo `BB.TrustStoreMock` project (Aspire
resolves the endpoint at runtime), so trust sync works with zero setup —
no Node mock, no manual URL. Point it at the real BB endpoint instead with:
`dotnet user-secrets set "Parameters:trust-store-base-url" "http://..." --project src/Host/SBQR.AppHost`.

First run pulls the Postgres container image, so it can take a minute or two;
subsequent runs are fast. Postgres data persists in a Docker volume across
restarts (same idea as compose's `sbqr.pgdata` volume) — delete it with
`docker volume rm sbqr.apphost-<hash>-sbqr-postgres-data` (find the exact
name via `docker volume ls`) if you need a clean database.

Database schema is **not** applied automatically here either — same as
docker-compose. Apply it with the external migration tool described in
`db/migrations/README.md` / `docs/docker/DEPLOYMENT.md` before hitting any
endpoint that touches the database.

Stop everything with `Ctrl+C` in the terminal running the AppHost.

## Local secrets

Most of what `SBQR.Api` needs to boot is wired up for you in
`src/Host/SBQR.AppHost/AppHost.cs` with the same dev-only defaults
`docker-compose.yml` already uses (dev JWT signing key, `PlainFile` key
custody provider, trust-store sync cadence, etc.) — these are not real
secrets, just the same publicly-committed dev values, and `Program.cs`
refuses them outside Development.

Two things are real secrets and are **not** hardcoded anywhere — they default
to blank (the API boots fine without them; you only need them to exercise
S3-backed storage or the OAuth2 bootstrap-token endpoint):

| What | Set with |
|---|---|
| Real AWS S3 credentials (see [docs/dev-s3-guide.md](dev-s3-guide.md)) | `dotnet user-secrets set "Parameters:storage-access-key-id" "AKIA..." --project src/Host/SBQR.AppHost`<br>`dotnet user-secrets set "Parameters:storage-secret-access-key" "..." --project src/Host/SBQR.AppHost`<br>`dotnet user-secrets set "Parameters:storage-bucket-name" "..." --project src/Host/SBQR.AppHost`<br>(optionally `storage-service-url` if you're not using the default AWS endpoint) |
| OAuth2 bootstrap client secret hash (generate with `dotnet run --project src/Host/SBQR.Api -- --generate-bootstrap-secret`) | `dotnet user-secrets set "Parameters:bootstrap-client-secret-hash" "<the Argon2id hash it prints>" --project src/Host/SBQR.AppHost` |

These live in your per-user secrets store (never in git), exactly like the
`.env` / `dotnet user-secrets` pattern the README already documents for
running `SBQR.Api` directly — this is just the AppHost's own copy of that
same idea, keyed under `Parameters:*` instead of the app's own config keys.

## Troubleshooting

- **"applicationUrl must be an https address" on startup** — the AppHost's
  `launchSettings.json` sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` so the
  plain-http dashboard profile (matching this repo's no-dev-TLS-cert
  convention) works; if you added a new launch profile, carry that env var
  over.
- **Port already in use / can't start Postgres** — you likely still have
  `docker compose -f docker/docker-compose.yml` running; stop it first (see
  above).
- **`sbqr-api` stuck on "Waiting"** — it's waiting for `sbqr-app` /
  `sbqr-key-vault` to report healthy first; check
  those resources' logs in the dashboard for the actual error (most likely:
  Postgres still pulling/starting on a slow connection).
- **Storage or bootstrap-token calls fail** — you haven't set the
  corresponding secret yet; see the table above.
