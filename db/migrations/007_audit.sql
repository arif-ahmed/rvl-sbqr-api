-- =============================================================================
-- 007_audit.sql  [sbqr_app]
--
-- public.audit_logs — append-only, hash-chained audit log (C8, A5).
-- Agreed design (2026-09-08): the chain lives WITHIN audit_logs — no separate
-- chain-head table.
--
-- Single table, NO partitions (decision: keep one audit table; partitioning
-- can be reintroduced later as a new numbered migration if volume demands).
--
-- Chain protocol (application-side, inside ONE transaction):
--
--   1. Serialize writers per chain scope:
--        SELECT pg_advisory_xact_lock(
--                 hashtextextended(COALESCE($tenantId::text, 'SYSTEM'), 0));
--      Chain scope is per tenant ('SYSTEM' for platform events). Without the
--      lock, two concurrent inserts could read the same previous row and fork
--      the chain.
--
--   2. Reserve the sequence value and read the chain head:
--        SELECT nextval('public.audit_logs_seq');                       -- $seq
--        SELECT entry_hash FROM public.audit_logs
--         WHERE tenant_id IS NOT DISTINCT FROM $tenantId
--         ORDER BY sequence DESC LIMIT 1;                              -- $prev
--      ($prev = 64 '0' characters for the first entry in a scope)
--
--   3. Compute the hash. entry_hash = lowercase hex SHA-256 over the UTF-8
--      canonical JSON — snake_case keys sorted ascending, no whitespace,
--      metadata embedded as its (masked) string value — of:
--        { "correlation_id", "created_at" (ISO-8601 UTC `yyyy-MM-ddTHH:mm:ss.fffZ`,
--          app-supplied), "created_by", "event_type", "metadata",
--          "previous_hash", "resource_id", "resource_type", "sequence",
--          "tenant_id" }
--      This is exactly what SBQR.Modules.Audit.Infrastructure.AuditLogger
--      computes; the C# code and this comment must stay in sync.
--
--   4. INSERT with explicit created_at, sequence, previous_hash, entry_hash.
--
--   created_at and sequence have NO defaults on purpose: the application must
--   supply them explicitly so the hash always covers the stored values.
--
-- Tamper evidence: an UPDATE, DELETE or TRUNCATE breaks the chain at the next
-- row (its previous_hash no longer matches a recomputation). Mutation is also
-- physically blocked by trigger AND by grants (runtime role is INSERT/SELECT
-- only — see 008). The C8 tamper test = a verification job that walks each
-- scope by sequence and recomputes every entry_hash.
--
-- tenant_id is nullable by design: platform-level events (trust sync, key
-- rotation) have no tenant. All tenant-bearing events MUST set it (C5).
--
-- Idempotent: safe to re-run.
-- =============================================================================

-- Single transaction: all-or-nothing per file. Re-runs skip existing objects.
BEGIN;

CREATE SEQUENCE IF NOT EXISTS public.audit_logs_seq AS bigint;

CREATE TABLE IF NOT EXISTS public.audit_logs (
    audit_log_id   uuid NOT NULL,
    tenant_id      uuid,
    correlation_id uuid        NOT NULL,
    event_type     varchar(100) NOT NULL,
    resource_type  varchar(100),
    resource_id    varchar(100),
    metadata            text,            -- masked JSON string (C19); entity maps string
    created_by          varchar(200),
    created_at          timestamptz NOT NULL,  -- app-supplied UTC; hashed — no default
    sequence            bigint      NOT NULL DEFAULT nextval('public.audit_logs_seq'),
    previous_hash       char(64)    NOT NULL,  -- 64 zeros for the first entry in scope
    entry_hash          char(64)    NOT NULL,
    -- Present only because the shared audit-column convention maps them on
    -- the entity; audit rows are immutable so they are never written.
    modified_by         varchar(200),
    modified_at         timestamptz,
    is_active           boolean     NOT NULL DEFAULT true,
    CONSTRAINT pk_audit_logs PRIMARY KEY (audit_log_id)
);

-- Chain-head read (step 2 of the protocol): per-scope latest entry.
CREATE INDEX IF NOT EXISTS ix_audit_logs_tenant_sequence
    ON public.audit_logs (tenant_id, sequence DESC);

-- Query patterns: tenant history, correlation lookup, event dashboards.
CREATE INDEX IF NOT EXISTS ix_audit_logs_tenant_created
    ON public.audit_logs (tenant_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_audit_logs_correlation
    ON public.audit_logs (correlation_id);
CREATE INDEX IF NOT EXISTS ix_audit_logs_event_created
    ON public.audit_logs (event_type, created_at DESC);

-- Cheap time-range scans over the fastest-growing table.
CREATE INDEX IF NOT EXISTS ix_audit_logs_created_brin
    ON public.audit_logs USING brin (created_at);

-- -----------------------------------------------------------------------------
-- Physical immutability (C8): the database itself refuses mutation.
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION public.forbid_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'public.audit_logs is append-only: % is prohibited', TG_OP
        USING ERRCODE = 'check_violation';
END $$;

DROP TRIGGER IF EXISTS trg_audit_logs_immutable ON public.audit_logs;
CREATE TRIGGER trg_audit_logs_immutable
    BEFORE UPDATE OR DELETE OR TRUNCATE ON public.audit_logs
    FOR EACH STATEMENT
    EXECUTE FUNCTION public.forbid_mutation();

COMMIT;
