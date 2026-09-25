# sbqr_app — database migrations

External SQL migrations for the `sbqr_app` database (per C20: the runtime DB
user has no DDL; migrations are applied out-of-band by an owner/admin role,
never by the API). The separate `sbqr_key_vault` database (wrapped private-key
material) has its own script set — **not yet written**, see "Out of scope"
below.

Target: **PostgreSQL 16** (the version pinned in `docker/docker-compose.yml`).
Every script is idempotent and safe to re-run: each file runs in a single
transaction (`BEGIN;` … `COMMIT;` — all-or-nothing), and every
`CREATE` uses `IF NOT EXISTS` (function uses `OR REPLACE`, trigger is
dropped with `IF EXISTS` first), so re-running skips objects that already
exist instead of duplicating them or failing.

## Order

Files run in lexicographic order; do not renumber applied scripts — add new
numbered files instead.

| # | File | Contents |
|---|------|----------|
| 001 | `001_module_schemas.sql` | single-schema layout — all tables live in `public` (no per-module schemas) |
| 002 | `002_tenancy_and_identity.sql` | `public.tenants`, `public.tenant_configurations` |
| 003 | `003_generation.sql` | `public.qr_generations` — append-only; tenant-scoped idempotency partial unique index (A9) |
| 004 | `004_verification.sql` | `public.qr_validations` — replay guard unique constraint (C6), verdict/reason CHECKs (A11) |
| 005 | `005_institution_trust.sql` | `public.institution_keys`, `public.trust_sync_runs` |
| 006 | `006_key_custody.sql` | `public.crypto_keys` — custody catalog + public half; private bytes live in `sbqr_key_vault` |
| 007 | `007_audit.sql` | `public.audit_logs` — single table (no partitions), hash chain columns, immutability trigger |
| 008 | `008_roles_and_grants.sql` | `sbqr_app_owner` / `sbqr_app_runtime` roles, least-privilege grants |

## Applying

Local (docker compose from the repo root must be up):

```bash
for f in db/migrations/*.sql; do
  docker compose -f docker/docker-compose.yml exec -T sbqr.postgres \
    psql -U postgres -d sbqr_app -v ON_ERROR_STOP=1 -f - < "$f" || break
done
```

Or from a workstation with `psql`:

```bash
for f in db/migrations/*.sql; do
  psql "Host=localhost;Port=5432;Database=sbqr_app;Username=<owner-role>" \
    -v ON_ERROR_STOP=1 -f "$f" || break
done
```

Never apply with a superuser in production — use the owner/admin role.

## Application-side contract (must match the code)

- **UUID v7** for the PK of every hot/append-only table
  (`qr_generations`, `qr_validations`, `audit_logs`) — .NET
  `Guid.CreateVersion7()`. Time-ordered ids keep B-tree inserts (and the
  WAL) localized; random v4 ids scatter them — this is exactly what the
  72-hour soak would otherwise expose.
- **Audit chain writes** follow the 4-step protocol documented at the top of
  `007_audit.sql` (advisory lock → reserve sequence + read chain head →
  compute `entry_hash` over canonical JSON → insert with explicit
  `created_at` / `sequence`). First entry of a scope uses 64 `0` characters
  as `previous_hash`.
- **Idempotency (A9)**: `Idempotency-Key` is an optional request header.
  Requests that carry one are protected by the partial unique index; on
  violation `23505` the API returns 409 `DUPLICATE_IDEMPOTENCY_KEY` (the QR
  payload is never persisted, so the original cannot be replayed back).
- **Replay (C6)**: `POST /v1/qr/validate` requires `requestId` +
  `requestTimestamp`; the handler rejects timestamps outside ±5 min
  (`REQUEST_STALE`). On `23505` from `uq_qr_validations_replay`, respond
  `REQUEST_REPLAYED` / `REPLAY_DETECTED`. The replay rejection itself cannot
  write a second validation row (that is the guard), so it is persisted to
  `audit_logs` (`qr.validation.rejected`).
- **PEM normalization** before touching `institution_keys.public_key`:
  trim, LF line endings — the sync model treats the (institution_code,
  key_version) pair as the identity, and normalized PEM keeps the stored
  `public_key_sha256` fingerprints stable.
- **EF Core mappings** must set the schema for every entity:
  `entity.ToTable("qr_generations", "public")` etc.

## Audit log (public.audit_logs)

Single table, no partitions. If write volume ever demands partitioning,
reintroduce it as a new numbered migration.

## Future consideration — Row-Level Security (RLS) for tenant isolation

Recorded 2026-09-08 as a serious post-GA concern for data security. Today C5
(tenant isolation) is enforced by application-level `TenantId` filters, the
architecture tests, and the runtime role grants. RLS would add
**database-enforced** isolation underneath all of that:

- `ALTER TABLE ... ENABLE ROW LEVEL SECURITY` on every tenant-bearing table,
  with policies binding `tenant_id = current_setting('app.tenant_id')::uuid`;
  the API sets that GUC per transaction (`SET LOCAL app.tenant_id = ...`).
- The runtime role must stay a **non-owner** and must never receive
  `BYPASSRLS` — table owners and `BYPASSRLS` roles bypass RLS entirely. The
  current grant design already keeps `sbqr_app_runtime` a non-owner, so this
  precondition holds.
- `audit_logs.tenant_id` is nullable (platform events); its policy must allow
  the platform scope explicitly.
- Not enabled before GA: it needs EF Core connection/transaction plumbing and
  a full integration-test pass, which does not fit inside the freeze. Until
  then the enforcement points above remain the C5 gates.

## Out of scope / follow-ups

- **`sbqr_key_vault` schema** (wrapped private-key table) — separate script,
  separate grants, separate backup/restore drill (C23). Not in this set.
- The docker design notes 16 public-side tables; this set delivers the
  agreed ones (the trust sync's `institution_registries` was merged into
  `institution_keys` on 2026-09-09, so we land 8 unique tables). Additional
  tables land as new numbered scripts.
- CI wiring for secret scanning / SCA / SAST (C9–C11) remains a known,
  charter-level gap.
