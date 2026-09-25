# `docker/postgres/` — local PostgreSQL for SBQR development

This directory is mounted into the `sbqr.postgres` container in
`docker/docker-compose.yml`. It contains **only** first-boot database
provisioning; schema and migrations live elsewhere.

## What's here

| Path | Purpose |
|---|---|
| `init/00-create-databases.sql` | Runs **once** when the data directory is empty. Creates the two logical databases required by SBQR (`sbqr_app`, `sbqr_key_vault`). |

The official `postgres` Docker image executes every `*.sql` / `*.sh` file
mounted at `/docker-entrypoint-initdb.d/` in **lexicographic order** against
the value of `POSTGRES_DB` (default `postgres`) **before** opening the
listening socket. So this script's existence is the only thing standing
between `docker compose up` and a working pair of databases.

## Why two databases

`docs/design/database-design.md` mandates two physical databases:

- **`sbqr_app`** — public-side state: tenancy, client credentials
  (identity), QR generations / validations, the trust directory, key
  custody catalog, and the audit log — one PostgreSQL schema per module.
- **`sbqr_key_vault`** — private-side state: wrapped (encrypted) private-key
  material only. In production this lives on a **separate** PostgreSQL
  Flexible Server instance so a compromised application subnet cannot reach
  it without separately authenticating to the vault server.

Locally we colocate them on one container for developer convenience. The
**connection-string env-var keys** stay identical between local, staging,
and production, so no `appsettings.json` changes are needed when promoting
between environments.

## First-boot vs subsequent boots

The init script runs **only on a fresh data directory**. After the very
first start, the named volume `sbqr.pgdata` contains a fully-bootstrapped
cluster and `/docker-entrypoint-initdb.d/` is ignored.

To re-run the script (for example after editing `00-create-databases.sql` to
add a third database), **wipe the named volume**:

```bash
docker compose -f docker/docker-compose.yml down -v
docker compose -f docker/docker-compose.yml up -d
```

The `-v` flag removes the `sbqr.pgdata` named volume; without it, the old
databases persist and the init script never runs again.

## Running migrations

The SBQR.Api host does not bundle a migration runner. The canonical schema is
applied to the database by an external migration tool (see
`docs/docker/DEPLOYMENT.md` for the runbook covering local, staging, and
production).

For the local docker-compose stack, the typical flow is:

1. `pwsh docker/start-db.ps1` to bring up PostgreSQL (this script).
2. Apply the canonical `db/migrations/*.sql` chain in lexicographic order
   against `sbqr.postgres:5432`, database `sbqr_app` — the exact commands
   are in `db/migrations/README.md` (`sbqr_key_vault` has no migration
   script yet).
3. `docker compose -f docker/docker-compose.yml up -d sbqr.api` to start the
   API now that the schema is in place.

## Inspecting the databases

Via Adminer (recommended, browser UI):

1. Visit `http://localhost:8081/` (or whatever `ADMINER_HOST_PORT` is set to).
2. System: `PostgreSQL`, Server: `sbqr.postgres`, Username: `postgres`,
   Password: `postgres`, Database: `sbqr_app` (or `sbqr_key_vault`).

Via `psql` inside the container:

```bash
docker compose -f docker/docker-compose.yml exec sbqr.postgres \
    psql -U postgres -l
```

## Secrets

The default `POSTGRES_USER=postgres` / `POSTGRES_PASSWORD=postgres` are
**local-only**. Do not copy these values into staging or production
connection strings — those live in Azure Key Vault and are injected as
environment variables by the orchestrator (see `docs/docker/DEPLOYMENT.md`).
