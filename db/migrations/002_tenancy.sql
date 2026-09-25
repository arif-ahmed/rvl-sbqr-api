-- =============================================================================
-- 002_tenancy_and_identity.sql  [sbqr_app]
--
-- 2026-09-09: tenant_configurations moved physically from the `identity`
-- schema to the `tenancy` schema. The bounded-context OWNER of the row is
-- still the IdentityAccess module (IdentityAccess owns the credentials
-- lifecycle: provisioning, Argon2id hashing, last_used_at stamping, status
-- flips, secret rotation). The move is purely a physical relocation: it
-- co-locates the tenant-scoped rows with the tenant they belong to and
-- removes a schema that no longer owns any tables. The C# DbContext mapping
-- in SBQR.Modules.IdentityAccess.Infrastructure.IdentityDbContext is the
-- one to update to point at the new schema name (out of scope for this
-- migration file — see follow-up tasks).
--
-- public.tenants — settled with TL; columns untouched. Only the
-- UNIQUE index on institution_code is added (no column change).
--
-- public.tenant_configurations — auth material for the machine-to-machine
-- client-credentials flow. Physically relocated to tenancy on 2026-09-09;
-- the BC owner is still IdentityAccess. Cross-schema FK to public.tenants
-- is now an intra-schema FK (no extraction-debt note needed for that one).
--
-- File NAME stays `002_tenancy_and_identity.sql` per the README rule "do
-- not renumber applied scripts — add new numbered files instead".
--
-- Idempotent: safe to re-run.
-- =============================================================================

-- Single transaction: all-or-nothing per file. Re-runs skip existing objects.
BEGIN;

CREATE TABLE IF NOT EXISTS public.tenants (
    tenant_id         uuid NOT NULL,
    institution_name  varchar(200) NOT NULL,
    institution_code  varchar(6)   NOT NULL,
    status            varchar(20)  NOT NULL,
    created_by        varchar(200),
    created_at        timestamptz  NOT NULL DEFAULT now(),
    modified_by       varchar(200),
    modified_at       timestamptz,
    is_active         boolean      NOT NULL DEFAULT true,
    CONSTRAINT pk_tenants PRIMARY KEY (tenant_id)
);

-- C5/C16: institution_code is the join key between tenants and the trust
-- directory; uniqueness is enforced by the database, not application logic.
-- Index names mirror the EF declarations in TenantConfiguration so the
-- schema-drift test (SchemaModelDriftTests) compares equal.
CREATE UNIQUE INDEX IF NOT EXISTS ix_tenants_institution_code
    ON public.tenants (institution_code);
CREATE INDEX IF NOT EXISTS ix_tenants_status ON public.tenants (status);
CREATE INDEX IF NOT EXISTS ix_tenants_active ON public.tenants (is_active)
    WHERE is_active = TRUE;


-- 2026-09-09: physically relocated from `identity.tenant_configurations`
-- to `public.tenant_configurations`. Column set, CHECK constraint, FK and
-- all index names are unchanged — the move is a schema rename only.
--
-- BC ownership note: the IdentityAccess module is still the OWNER of the
-- credential lifecycle (provisioning, Argon2id hashing, status flips,
-- last_used_at stamping). Only the physical schema changed. The
-- `tenant_configurations` table is consumed by the FR-TENANT-001
-- activate-gate through `ITenantConfigurationProvisioner` (IdentityAccess
-- Contracts seam).
CREATE TABLE IF NOT EXISTS public.tenant_configurations (
    tenant_configuration_id   uuid NOT NULL,
    tenant_id                 uuid NOT NULL,
    client_id                 varchar(100) NOT NULL,
    -- Argon2id PHC string (algorithm + parameters + salt embedded).
    -- Never the plaintext secret, never a reversible cipher (C3/C9).
    client_secret_hash        text NOT NULL,
    is_qr_generation_allowed  boolean NOT NULL DEFAULT true,
    is_qr_validation_allowed  boolean NOT NULL DEFAULT true,
    status                    varchar(20) NOT NULL DEFAULT 'ACTIVE',
    expires_at                timestamptz,
    last_used_at              timestamptz,
    created_by                varchar(200),
    created_at                timestamptz NOT NULL DEFAULT now(),
    modified_by               varchar(200),
    modified_at               timestamptz,
    is_active                 boolean NOT NULL DEFAULT true,
    CONSTRAINT pk_tenant_configurations PRIMARY KEY (tenant_configuration_id),
    CONSTRAINT fk_tenant_configurations_tenant
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (tenant_id) ON DELETE RESTRICT,
    -- IdentityAccess maps one more status (PENDING_ROTATION) than the
    -- original agreed trio; keep the constraint in sync with
    -- TenantConfigurationConfiguration.ToDb/FromDb.
    CONSTRAINT ck_tenant_configurations_status
        CHECK (status IN ('ACTIVE', 'SUSPENDED', 'REVOKED', 'EXPIRED', 'PENDING_ROTATION'))
);

-- Auth hot path: every API request resolves client_id -> tenant + flags.
-- Must be an index hit, never a scan. Names mirror the EF declarations in
-- TenantConfigurationConfiguration.
CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_configurations_client_id_unique
    ON public.tenant_configurations (client_id);

-- FK support index (Postgres does not auto-index FK columns).
CREATE INDEX IF NOT EXISTS ix_tenant_configurations_tenant
    ON public.tenant_configurations (tenant_id);
CREATE INDEX IF NOT EXISTS ix_tenant_configurations_status
    ON public.tenant_configurations (status);
CREATE INDEX IF NOT EXISTS ix_tenant_configurations_expires_at
    ON public.tenant_configurations (expires_at)
    WHERE expires_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_tenant_configurations_active
    ON public.tenant_configurations (is_active)
    WHERE is_active = TRUE;

COMMIT;
