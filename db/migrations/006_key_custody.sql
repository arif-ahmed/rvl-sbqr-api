-- =============================================================================
-- 006_key_custody.sql  [sbqr_app]
--
-- public.crypto_keys — the signing-key catalog for our tenants,
-- following the implemented KeyCustody lifecycle (GENERATING..RETIRED).
--
-- NOTE on the 2026-09-08 decision "drop public_key / public_key_sha256 from
-- crypto_keys (read them from institution_keys instead)": DEFERRED, not
-- dropped. The implemented Generate/Adopt factories, domain events, custody
-- providers, API summaries and tests all consume both columns today.
-- Removing them requires the coordinated KeyCustody->InstitutionTrust
-- publish flow (institution_keys.source = 'LOCAL' exists for exactly that).
-- Until that refactor lands, this table keeps both columns; the private key
-- NEVER enters sbqr_app (only custody_key_reference, an opaque vault/HSM
-- handle — the wrapped private bytes live in the separate sbqr_key_vault
-- database). See docs/design/database-design.md "deferred deltas".
--
--   key_id             logical per-tenant signing-key name (NOT globally
--                     unique — every tenant's first key is 'sbqr-signing');
--                     uniqueness is (tenant_id, key_id, key_version).
--   custody_key_reference  opaque handle: tenant:<guid>:institution:<6
--                     digits>:<keyId>:v<n>. Resolved by KeyCustody to
--                     call the custody backend's sign API.
--   valid_from / valid_to / rotated_at  C4 gate (rotation <= 90 days):
--                     the rotation policy computes valid_to = valid_from +
--                     90d at activation; alerting checks it.
--
-- Idempotent: safe to re-run.
-- =============================================================================

-- Single transaction: all-or-nothing per file. Re-runs skip existing objects.
BEGIN;

CREATE TABLE IF NOT EXISTS public.crypto_keys (
    crypto_key_id         uuid NOT NULL,
    tenant_id             uuid NOT NULL,
    key_id                varchar(100) NOT NULL,
    key_version           integer      NOT NULL,
    public_key            text         NOT NULL,
    custody_key_reference varchar(500) NOT NULL,
    status                varchar(20)  NOT NULL DEFAULT 'PENDING',
    valid_from            timestamptz  NOT NULL DEFAULT now(),
    valid_to              timestamptz,
    rotated_at            timestamptz,
    public_key_sha256     char(64)     NOT NULL,
    created_by            varchar(200),
    created_at            timestamptz  NOT NULL DEFAULT now(),
    modified_by           varchar(200),
    modified_at           timestamptz,
    is_active             boolean      NOT NULL DEFAULT true,
    CONSTRAINT pk_crypto_keys PRIMARY KEY (crypto_key_id),
    CONSTRAINT fk_crypto_keys_tenant
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (tenant_id) ON DELETE RESTRICT,
    -- Vocabulary matches CryptoKeyConfiguration.ToDb/FromDb exactly.
    CONSTRAINT ck_crypto_keys_status CHECK (status IN (
        'GENERATING', 'PENDING', 'ACTIVE', 'SUSPENDED', 'RETIRING', 'RETIRED', 'REVOKED'))
);

-- One row per (tenant, logical key name, version) — name mirrors the EF
-- declaration in CryptoKeyConfiguration.
CREATE UNIQUE INDEX IF NOT EXISTS ix_crypto_keys_tenant_keyid_version_unique
    ON public.crypto_keys (tenant_id, key_id, key_version);

-- At most one ACTIVE signing key per tenant — the activation gate the QR
-- generation path relies on.
CREATE UNIQUE INDEX IF NOT EXISTS ix_crypto_keys_active_tenant_unique
    ON public.crypto_keys (tenant_id)
    WHERE status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS ix_crypto_keys_status
    ON public.crypto_keys (status);

CREATE INDEX IF NOT EXISTS ix_crypto_keys_active
    ON public.crypto_keys (is_active) WHERE is_active = TRUE;

COMMIT;
