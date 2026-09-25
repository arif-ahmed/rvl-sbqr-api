-- =============================================================================
-- 005_institution_trust.sql  [sbqr_app]
--
-- Single versioned-key table carrying the cryptographic publication of each
-- institution's public key. Institutional identity (institution_code → name,
-- display, etc.) lives in public.tenants joined on institution_code; this
-- row carries the cryptographic publication PLUS a denormalised display name
-- and Annex A Institution Type so a verifier resolving a QR gets the
-- issuer's identity without joining public.tenants.
--
-- The legacy institution_registries table has been dropped — its columns
-- were folded into institution_keys (institution_name, institute_type).
--
-- Platform-level trust data — deliberately NO tenant_id (load-bearing
-- assertion L5 in the module): the trust-directory and sync runs are shared
-- across all tenants.
--
--   institution_code      6-digit BB institution code (the join key to
--                         public.tenants.institution_code).
--   institute_type        2-digit Annex A Institution Type (Tag 26 sub 01).
--                         Derivable from institution_code[..2] but carried
--                         explicitly on the row so the verifier path never
--                         re-derives a value that may have been corrected
--                         out-of-band by BB (the trust-store is the
--                         source of truth).
--   institution_name      Human-readable display name (NOT NULL — see the
--                         ck_institution_keys_name_nonblank CHECK).
--   source                REGISTRY (synced from the BB registry) or LOCAL
--                         (one of OUR tenants' signing keys — reserved for
--                         the KeyCustody publish flow; protects own keys
--                         from being clobbered by the registry sync).
--   status                ACTIVE | SUSPENDED | RETIRED. Suspension comes
--                         from the admin surface; retirement comes from a
--                         new key version superseding the old. SUSPENDED is
--                         a future state — currently no admin endpoint
--                         flips it.
--   key_version           monotonic per institution; UNIQUE
--                         (institution_code, key_version). New sync => next
--                         version; previous ACTIVE key flips to RETIRED.
--   public_key_sha256     SHA-256 hex of the PEM ASCII bytes. Cheap
--                         comparison key for sync reconciliation and the
--                         Adopt-mode drift guard.
--   valid_from / valid_to / revoked_at / synced_at
--                         C4/C16 gates: "expired or revoked -> reject" is
--                         only evaluable with a persisted validity window.
--                         valid_from opens the window at publish time;
--                         valid_to stays null until revocation; revoked_at
--                         is the moment the trust store reported a
--                         non-ACTIVE status.
--
-- Idempotent: safe to re-run.
-- =============================================================================

-- Single transaction: all-or-nothing per file. Re-runs skip existing objects.
BEGIN;

CREATE TABLE IF NOT EXISTS public.institution_keys (
    institution_key_id  uuid         NOT NULL,
    institution_code    varchar(6)   NOT NULL,
    institute_type      varchar(2)   NOT NULL,
    institution_name    varchar(200) NOT NULL,
    status              varchar(20)  NOT NULL DEFAULT 'ACTIVE',
    key_version         integer      NOT NULL,
    public_key          text         NOT NULL,
    public_key_sha256   char(64)     NOT NULL,
    source              varchar(20)  NOT NULL DEFAULT 'REGISTRY',
    valid_from          timestamptz  NOT NULL DEFAULT now(),
    valid_to            timestamptz,
    revoked_at          timestamptz,
    synced_at           timestamptz  NOT NULL DEFAULT now(),
    is_active           boolean      NOT NULL DEFAULT true,
    created_by          varchar(200),
    created_at          timestamptz  NOT NULL DEFAULT now(),
    modified_by         varchar(200),
    modified_at         timestamptz,
    CONSTRAINT pk_institution_keys PRIMARY KEY (institution_key_id),
    CONSTRAINT ck_institution_keys_source
        CHECK (source IN ('REGISTRY', 'LOCAL')),
    -- Final vocabulary includes 'REVOKED' (compromised — fail closed,
    -- must never verify). This inline CHECK carries the FULL vocabulary
    -- because 004_verification.sql's constraint-replacement block is
    -- table-existence-guarded and skips on a fresh chain (004 runs before
    -- this table exists).
    CONSTRAINT ck_institution_keys_status
        CHECK (status IN ('ACTIVE', 'SUSPENDED', 'RETIRED', 'REVOKED')),
    CONSTRAINT ck_institution_keys_name_nonblank
        CHECK (length(trim(institution_name)) > 0),
    CONSTRAINT ck_institution_keys_institute_type
        CHECK (institute_type ~ '^[0-9]{2}$')
);

-- Versioned resolution: one row per (institution_code, key_version).
-- Name mirrors the EF declaration in InstitutionTrustDbContext.
CREATE UNIQUE INDEX IF NOT EXISTS ix_institution_keys_institution
    ON public.institution_keys (institution_code, key_version);

-- Signer resolution on the verify hot path: lookup by institution_code
-- alone (the resolver picks the ACTIVE key version for that code).
CREATE INDEX IF NOT EXISTS ix_institution_keys_code
    ON public.institution_keys (institution_code);

-- Status partial index: cheap "is there an ACTIVE row for this code?"
-- probe for the activate-gate's GetInstitutionPublicKeyQuery.
CREATE INDEX IF NOT EXISTS ix_institution_keys_code_active
    ON public.institution_keys (institution_code)
    WHERE status = 'ACTIVE' AND is_active = TRUE;

-- -----------------------------------------------------------------------------
-- public.trust_sync_runs — operational history of the registry
-- sync job. Platform-level (no tenant).
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.trust_sync_runs (
    run_id               uuid NOT NULL,
    started_at           timestamptz NOT NULL DEFAULT now(),
    finished_at          timestamptz,
    status               varchar(20)  NOT NULL DEFAULT 'RUNNING',
    institutions_synced  integer      NOT NULL DEFAULT 0,
    institutions_added   integer      NOT NULL DEFAULT 0,
    institutions_updated integer      NOT NULL DEFAULT 0,
    institutions_revoked integer      NOT NULL DEFAULT 0,
    triggered_by         varchar(200),
    error_message        text,
    CONSTRAINT pk_trust_sync_runs PRIMARY KEY (run_id),
    CONSTRAINT ck_trust_sync_runs_status
        CHECK (status IN ('RUNNING', 'SUCCEEDED', 'FAILED', 'PARTIAL'))
);

CREATE INDEX IF NOT EXISTS ix_trust_sync_runs_started
    ON public.trust_sync_runs (started_at DESC);

COMMIT;
