# BB.TrustStoreMock — BB Trust Store mock

**Temporary dev/test stand-in** for the Bangladesh Bank public-key trust
store described in `docs/bb-banglaqr-p2p-specification.md` Annex B. BB has
not published a real contract; this service is our best-guess simulation,
shaped from the spec's own language ("retrieve the public key file using
the derived Institution_ID"). When BB's real trust store lands:

1. point `TrustStore:BaseUrl` (SBQR.Api) at the real endpoint,
2. delete this project and its compose service,
3. `DROP DATABASE bb_trust_store_mock;`

It is a separate deployable that simulates an EXTERNAL system — never
referenced by `SBQR.Api`, never an `IModule`. It hard-fails at startup in
the `Production` environment.

## Endpoints

| Verb | Route | Purpose |
|---|---|---|
| PUT | `/trust-store/institutions/{id}/public-key` | **Upload the public key for an institution** (spec Annex B/C — "share their public key with Bangladesh Bank", one key file per Institution_ID). `201` when a new key version was minted, `200` when the upload was an idempotent no-op (same key material). |
| GET | `/trust-store/institutions/{id}/public-key` | Resolve the currently trusted key for one institution (spec Annex B step 2). 404 when there is no ACTIVE key. |
| GET | `/trust-store/institutions` | Full directory feed (what `HttpTrustStoreClient` syncs from). Revoked institutions stay listed with `activeKey.status = "REVOKED"` — revocation is explicit, never inferred from absence. |
| POST | `/admin/institutions/{id}/keys/revoke` | Mock-only: mark the active key `REVOKED` (204; 404 if the institution is unknown). The spec defines the revoked outcome (Annex B Step 3) but no mechanism — withdrawing trust is out-of-band for the real BB. |
| GET | `/scalar` · `/openapi/v1.json` | Contract docs — the artifact to hand BB when negotiating the real service. |
| GET | `/health` | Liveness probe — returns `{"status":"ok"}`. Used by Render's health check (no DB call). |

## Upload contract

```
PUT /trust-store/institutions/031008/public-key
Content-Type: application/json

{
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----",
  "institutionName": "Example PSP",
  "instituteType": "03",
  "validFrom": "2026-09-12T00:00:00Z",
  "validTo": null
}
```

- The `{id}` route parameter — exactly 6 digits: the Annex B Institution_ID
  (Tag 26 sub-tag 01 institution type ‖ sub-tag 02 institution id).
- `institutionName` — required, ≤ 200 chars.
- `instituteType` — optional 2 digits; must match the id prefix when given.
- `publicKeyPem` — **Ed25519 public key in SPKI PEM form only** (spec
  Annex C + constraint C2). RSA/EC/private keys/garbage → 400.
- `validFrom`/`validTo` — optional passthrough metadata, stored and echoed
  verbatim; the mock never derives status from them (the temporal gate
  belongs to InstitutionTrust).

Re-uploading the same key material is an idempotent no-op (no version bump);
uploading different key material retires the previous version and increments
`keyVersion`.

### Generating a spec-conformant key (Annex C)

```bash
openssl genpkey -algorithm ed25519 -out 031008-private.pem
openssl pkey -in 031008-private.pem -pubout -out 031008-public.pem
# the contents of 031008-public.pem are what you upload
```

## Persistence

Uploaded keys live in this mock's OWN disposable PostgreSQL database,
`bb_trust_store_mock` (tables `institutions` + `institution_keys` in the
public schema, created by the service itself at startup via `EnsureCreated`).
Restarting the container does NOT wipe uploads — that is the point.

- Created on fresh volumes by
  `docker/postgres/init/00-create-databases.sql`. On an existing dev volume,
  `docker/start-db.ps1` creates it automatically if missing (or create it
  once manually:
  `docker compose -f docker/docker-compose.yml exec sbqr.postgres psql -U postgres -c "CREATE DATABASE bb_trust_store_mock;"`)
- Inspect it via Adminer: `http://localhost:8081/?pgsql=sbqr.postgres&username=postgres&db=bb_trust_store_mock`
- Wipe the data (not the schema) with
  `TRUNCATE institution_keys, institutions RESTART IDENTITY CASCADE;`

### Configuration (never in tracked files — C9)

| Key | Source | Notes |
|---|---|---|
| `ConnectionStrings:bb_trust_store_mock` | **Defaults to the disposable local-dev value in this repo's `appsettings.json`** (`localhost:5432`, `postgres/postgres` — a documented non-secret, same value as the tracked compose file and seed scripts). Overridden by user-secrets or the compose env var (`ConnectionStrings__bb_trust_store_mock`, which points at `sbqr.postgres` inside the docker network). `EnsureCreated` provisions the database and schema on first boot. |

No authentication — the mock is open by design (dev/test only, refuses to run in Production). If InstitutionTrust's client is configured with `TrustStore:ApiKey`, it sends an `X-Api-Key` header the mock simply ignores.

## Running

```bash
# Compose (recommended) — mock + Postgres + SBQR.Api + Adminer:
docker compose -f docker/docker-compose.yml up --build
# mock is published on http://localhost:8082

# Or standalone (Postgres must be up, user-secrets seeded):
dotnet run --project src/Host/BB.TrustStoreMock   # → http://localhost:5002
```

## Tests

`tests/SBQR.BbTrustStoreMock.Tests` — in-process `WebApplicationFactory`
against an ephemeral Testcontainers Postgres (Docker required): upload
validation (400s incl. the Ed25519-only gate), read happy paths, idempotent
uploads, rotation, revocation propagation, API-key enforcement, and
persistence across app instances.

## Deploying to Render

The repo includes a `render.yaml` at the root that provisions a free web
service + free Postgres and wires the connection string automatically.
Render has no native .NET runtime, so the build runs through a
multi-stage Dockerfile in this project.

```bash
# Push the project to a Git branch, then in the Render dashboard:
#   New → Blueprint → point at this repo → Apply.
# Render creates the DB, builds the image, deploys the service, and
# injects ConnectionStrings__bb_trust_store_mock as an env var.
# After ~2 minutes the service is live at https://<name>.onrender.com.
```

> Note: Render free-tier web services sleep after 15 minutes of inactivity
> and incur a ~30s cold start on the next request. Free Postgres is
> deleted after 90 days of disuse — acceptable for a disposable mock,
> replace with a paid plan if you need to keep it warm.
