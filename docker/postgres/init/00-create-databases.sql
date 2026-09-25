-- =============================================================================
-- 00-create-databases.sql
--
-- Runs ONCE on first container start of `sbqr.postgres`. The official
-- postgres Docker image executes every *.sql / *.sh file mounted under
-- /docker-entrypoint-initdb.d/ in lexicographic order against the value of
-- POSTGRES_DB (default: "postgres") before opening the port for clients.
--
-- SBQR has TWO logical databases (per docs/design/database-design.md):
--   - sbqr_app         : 9 public-side tables across the per-module schemas
--                        (tenancy, identity, generation, verification,
--                        institution_trust, key_custody, audit) — created by
--                        db/migrations/*.sql, NOT by this script
--   - sbqr_key_vault   : wrapped private-key material only (schema script TBD)
--
-- Production keeps the SBQR databases on two separate Flexible Server
-- instances for blast-radius isolation; locally we colocate them on one
-- Postgres so the developer's docker desktop has one container to manage.
--
-- This script is IDEMPOTENT-ON-FRESH-VOLUME ONLY. If the named volume
-- already exists with the databases, the postgres entrypoint will skip
-- /docker-entrypoint-initdb.d/ entirely (the data directory check happens
-- first). To re-run this script you MUST wipe the volume:
--
--     docker compose -f docker/docker-compose.yml down -v
-- =============================================================================

CREATE DATABASE sbqr_app;
CREATE DATABASE sbqr_key_vault;
