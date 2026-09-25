-- =============================================================================
-- 008_tenant_applications.sql  [sbqr_app]
--
-- public.tenant_applications — per-tenant allow-list of registered mobile-app
-- package identifiers, introduced by FR-AUTH-002 and extended by FR-AUTH-003.
-- Each row is one registered app (Android applicationId or iOS bundle ID) for
-- one tenant. The IdentityAccess OAuth 2.1 token endpoint resolves the claimed
-- `package_id` against this table at mint time so a wrong / unregistered /
-- suspended app fails closed with audit reason `package_mismatch`.
--
-- Why this lives in `tenancy` and not a new `apps` schema: the row is owned
-- by the same bounded context that owns the tenant (registration, suspension,
-- lifecycle) — adding a sibling schema would split a single lifecycle across
-- two physical schemas for no operational benefit. The token-endpoint consumer
-- reads through a Contracts seam (ITenantApplicationDirectory), the same way
-- it reads tenant admission state through ITenantAdmissionDirectory.
--
-- Column evolution:
--   * FR-AUTH-002 (MVP):  platform + package_id only. The token endpoint
--     checks package_id against this table. This alone cannot detect a
--     repackaged APK (same package_id, re-signed).
--   * FR-AUTH-003 (attestation):  adds signing_cert_sha256. The attestation
--     verifier compares the digest from Google/Apple's verdict against this
--     column. A mismatch means the APK was re-signed — enrollment is rejected.
--
-- Design rules:
--   * status / platform are CHECK-constrained (no string drift in writes)
--   * (platform, package_id) is UNIQUE platform-wide so two tenants cannot
--     claim the same app on the same store. Same string on both platforms for
--     one tenant is legal (two rows).
--   * signing_cert_sha256 is nullable — existing apps registered under
--     FR-AUTH-002 may not have it. NULL means "not yet registered for
--     attestation" (the package_id check still applies at the token endpoint).
--   * FK to public.tenants with ON DELETE RESTRICT — a tenant with registered
--     apps must be terminated (not deleted) before its row goes away.
--   * Active partial index on is_active = TRUE so the auth hot path skips the
--     is_active filter at runtime; mirror of ix_tenants_active and
--     ix_tenant_configurations_active.
--
-- Idempotent: safe to re-run.
-- =============================================================================

-- Single transaction: all-or-nothing per file. Re-runs skip existing objects.
BEGIN;

CREATE TABLE IF NOT EXISTS public.tenant_applications (
    tenant_application_id  uuid         NOT NULL,
    tenant_id              uuid         NOT NULL,
    platform               varchar(10)  NOT NULL,
    package_id             varchar(200) NOT NULL,
    status                 varchar(20)  NOT NULL DEFAULT 'ACTIVE',
    created_by             varchar(200),
    created_at             timestamptz  NOT NULL DEFAULT now(),
    modified_by            varchar(200),
    modified_at            timestamptz,
    is_active              boolean      NOT NULL DEFAULT true,
    CONSTRAINT pk_tenant_applications PRIMARY KEY (tenant_application_id),
    CONSTRAINT fk_tenant_applications_tenant
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (tenant_id) ON DELETE RESTRICT,
    CONSTRAINT ck_tenant_applications_platform
        CHECK (platform IN ('ANDROID', 'IOS')),
    CONSTRAINT ck_tenant_applications_status
        CHECK (status IN ('ACTIVE', 'SUSPENDED'))
);


-- Platform-wide uniqueness: two different tenants cannot claim the same app
-- on the same store. The same reverse-DNS string on ANDROID and IOS for one
-- tenant (the common case where an FI ships one app on both stores with the
-- same applicationId/bundleId) is legal — that's two rows.
CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_applications_platform_package_id
    ON public.tenant_applications (platform, package_id);

-- FK support index (Postgres does not auto-index FK columns).
CREATE INDEX IF NOT EXISTS ix_tenant_applications_tenant
    ON public.tenant_applications (tenant_id);

-- Auth hot path: every /v1/oauth/token call with a claimed package_id hits
-- this index. Also used by the attestation verifier (FR-AUTH-003) to fetch
-- the signing_cert_sha256 golden record.
CREATE INDEX IF NOT EXISTS ix_tenant_applications_active
    ON public.tenant_applications (is_active)
    WHERE is_active = TRUE;

-- Attestation verification hot path: the verifier queries by
-- (tenant_id, platform, package_id, is_active) to fetch the golden record,
-- then compares signing_cert_sha256 in-memory against the verdict.
CREATE INDEX IF NOT EXISTS ix_tenant_applications_tenant_platform_package
    ON public.tenant_applications (tenant_id, platform, package_id, is_active)
    WHERE is_active = TRUE;

COMMIT;
