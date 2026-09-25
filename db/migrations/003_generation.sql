-- =============================================================================
-- 003_generation.sql  [sbqr_app]
-- (renamed from 003_issuance.sql on 2026-09-08; schema renamed issuance ->
--  generation so file, schema and QrGeneration module naming stay consistent)
--
-- public.qr_generations — one row per successful QR generation.
-- APPEND-ONLY: modified_by / modified_at / is_active from the draft are
-- intentionally dropped (a generation is an immutable financial event;
-- soft-delete columns on it are false mutability signals).
--
-- payload_hash is intentionally absent (decision 2026-09-08): integrity is
-- provided by the QR signature + CRC, not by a stored hash.
--
-- signature_key_version kept (decision: nice-to-have): the key_version of the
-- tenant signing key (public.crypto_keys) that signed this QR. Enables
-- post-rotation verification and blast-radius queries when a key version is
-- retired. Weak reference by design — no FK into key_custody (module boundary).
--
-- idempotency_key (A9): client-supplied (Idempotency-Key header, OPTIONAL in
-- the current API contract), tenant-scoped, enforced by a DB partial UNIQUE
-- constraint — the race between concurrent retries is closed by the
-- database, not application logic. A duplicate insert raises 23505 which
-- the handler converts into a 409 DUPLICATE_IDEMPOTENCY_KEY (the QR payload
-- itself is never persisted, so the original cannot be replayed back).
-- Rows without a key are exempt from uniqueness (Postgres partial index).
--
-- Idempotent: safe to re-run.
-- =============================================================================

-- Single transaction: all-or-nothing per file. Re-runs skip existing objects.
BEGIN;

CREATE TABLE IF NOT EXISTS public.qr_generations (
    qr_generation_id       uuid NOT NULL,
    tenant_id              uuid NOT NULL,
    qr_type                varchar(10)  NOT NULL,
    signature_key_version  integer      NOT NULL,
    idempotency_key        varchar(100),
    created_by             varchar(200),
    created_at             timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT pk_qr_generations PRIMARY KEY (qr_generation_id),
    CONSTRAINT fk_qr_generations_tenant
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (tenant_id) ON DELETE RESTRICT,
    -- Align vocabulary with the BanglaQR spec team; values listed so drift
    -- fails fast instead of producing six spellings.
    CONSTRAINT ck_qr_generations_qr_type CHECK (qr_type IN ('STATIC', 'DYNAMIC'))
);

-- A9 gate: idempotency is tenant-scoped and DB-enforced; only requests
-- that carry a key participate (the header is optional in the API).
CREATE UNIQUE INDEX IF NOT EXISTS uq_qr_generations_tenant_idempotency
    ON public.qr_generations (tenant_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

-- Tenant dashboards / monitoring pagination.
CREATE INDEX IF NOT EXISTS ix_qr_generations_tenant_created
    ON public.qr_generations (tenant_id, created_at DESC);

-- Blast-radius query: "every code signed with key version N of tenant T".
CREATE INDEX IF NOT EXISTS ix_qr_generations_tenant_key_version
    ON public.qr_generations (tenant_id, signature_key_version);

COMMIT;
