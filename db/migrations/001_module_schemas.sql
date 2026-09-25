-- =============================================================================
-- 001_module_schemas.sql  [sbqr_app]
--
-- Single-schema layout: ALL tables live in the `public` schema. There is no
-- per-module schema in this deployment (tenancy, generation, verification,
-- institution_trust, key_custody, audit schemas are NOT created or used).
--
-- Tables (all in public):
--   tenants, tenant_configurations,
--   qr_generations,
--   qr_validations,
--   institution_keys, trust_sync_runs,
--   crypto_keys,
--   audit_logs (single table, no partitions),
--   tenant_applications, enrolled_devices.
--
-- Target: PostgreSQL 16+. Idempotent: safe to re-run.
-- =============================================================================

-- Single transaction: all-or-nothing per file. Re-runs skip existing objects.
BEGIN;

CREATE SCHEMA IF NOT EXISTS public;

COMMENT ON SCHEMA public IS 'Single-schema layout: all SBQR tables live here (no per-module schemas).';

COMMIT;
