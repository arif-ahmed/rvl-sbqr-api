-- =============================================================================
-- 004_verification.sql (consolidated)  [sbqr_app]
--
-- public.qr_validations — one row per processed verification request
-- (A5: success AND failure are persisted). This table is ALSO the C6 replay
-- guard: the UNIQUE (tenant_id, request_id) index makes each request
-- single-use; a duplicate insert raises 23505 which the application converts
-- into a REQUEST_REPLAYED / REPLAY_DETECTED response. Because the second row
-- can never be inserted (that is the point), the replay rejection itself is
-- audited in audit_logs (event qr.validation.rejected), not here.
--
--   tenant_id          C5: the VERIFYING tenant (who called the API), resolved
--                      server-side from the authenticated client credential
--                      (ICurrentTenant). Never taken from the request body.
--   request_id         C6: client-generated unique id per verification call.
--   request_timestamp  C6: client clock at send time; the handler validates a
--                      ±5 min window against server time (depends on C14
--                      NTP sync) — stale => REQUEST_STALE / TIMESTAMP_STALE.
--   correlation_id     A5: server-generated per request; also embedded in the
--                      audit metadata so validation row and audit rows join.
--   institution_code   the ISSUER code extracted from the QR payload.
--                      Nullable on purpose: a malformed payload has no
--                      extractable issuer and the row must still persist.
--   verdict            stable vocabulary of the implemented pipeline
--                      (QrVerdict enum) plus the two request-level outcomes
--                      (REQUEST_STALE, REQUEST_REPLAYED) for C6.
--   trust_source       which authority decided: TRUST_DIRECTORY (issuer key
--                      resolved via institution_keys per spec Annex B), or
--                      NONE (not evaluated — structural / non-P2P / stale /
--                      replayed). OWN_CUSTODY removed (Verification no longer
--                      has a dual-path — every issuer resolves through the
--                      trust store).
--   reason_code        A11: stable machine-readable rejection code; mandatory
--                      for every non-passing verdict (NON_P2P is
--                      informational, not a rejection).
--
-- qr_transaction_id from the draft is REMOVED (agreed): the referenced table
-- does not exist, foreign-issued codes can never reference our
-- qr_generations table, and verification -> generation would be a
-- cross-module FK. The qr_transactions table is gone entirely; this single
-- table records the attempt AND its outcome.
-- payload_hash is not persisted (decision 2026-09-08): integrity is provided
-- by the QR signature + CRC; the computed hash still flows to the API
-- response and audit resource_id for correlation.
-- Append-only: modified_* / is_active dropped.
--
-- Consolidated from 007_verification_consolidation.sql:
--   - Expanded institution_keys.status CHECK to include REVOKED
--     (ACTIVE | SUSPENDED | RETIRED | REVOKED).
--   - Removed OWN_CUSTODY from ck_qr_validations_trust_source
--     (now TRUST_DIRECTORY | NONE only).
--   - Added ix_institution_keys_code_retired partial index for the
--     historical-key retry path (Phase 4 fallback).
--
-- Idempotent: safe to re-run.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- 1. public.qr_validations  (C5 + C6 + A5 + A11 + C13)
-- ---------------------------------------------------------------------------

-- Single transaction: all-or-nothing per file. Re-runs skip existing objects.
BEGIN;

CREATE TABLE IF NOT EXISTS public.qr_validations (
    qr_validation_id   uuid NOT NULL,
    tenant_id          uuid NOT NULL,
    request_id         varchar(100) NOT NULL,
    request_timestamp  timestamptz  NOT NULL,
    correlation_id     uuid         NOT NULL,
    institution_code   varchar(6),
    verdict            varchar(30)  NOT NULL,
    trust_source       varchar(20)  NOT NULL DEFAULT 'NONE',
    reason_code        varchar(60),
    created_by         varchar(200),
    created_at         timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT pk_qr_validations PRIMARY KEY (qr_validation_id),
    CONSTRAINT fk_qr_validations_tenant
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (tenant_id) ON DELETE RESTRICT,
    CONSTRAINT ck_qr_validations_verdict CHECK (verdict IN (
        'VALID', 'INVALID_SIGNATURE', 'STRUCTURAL_INVALID', 'KEY_NOT_FOUND',
        'KEY_SUSPENDED', 'KEY_REVOKED', 'KEY_NOT_ACTIVE', 'NON_P2P',
        'REQUEST_STALE', 'REQUEST_REPLAYED')),
    CONSTRAINT ck_qr_validations_trust_source
        CHECK (trust_source IN ('TRUST_DIRECTORY', 'NONE')),
    CONSTRAINT ck_qr_validations_reason_required
        CHECK (verdict IN ('VALID', 'NON_P2P') OR reason_code IS NOT NULL)
);

-- C6 gate: single-use verification requests, DB-enforced, tenant-scoped.
CREATE UNIQUE INDEX IF NOT EXISTS uq_qr_validations_replay
    ON public.qr_validations (tenant_id, request_id);

-- Tenant monitoring / pagination (also serves the FK lookups on tenant_id).
CREATE INDEX IF NOT EXISTS ix_qr_validations_tenant_created
    ON public.qr_validations (tenant_id, created_at DESC);

-- Failure dashboards and VAPT evidence: rejections only, cheap partial index.
CREATE INDEX IF NOT EXISTS ix_qr_validations_tenant_failures
    ON public.qr_validations (tenant_id, verdict)
    WHERE verdict <> 'VALID';

-- ---------------------------------------------------------------------------
-- 2. public.institution_keys — status CHECK + partial index
--    (C16: signer must resolve in InstitutionTrust; expired/revoked/unknown
--     → reject.)
--
--    KeyCustody publishes status transitions (suspend / retire / revoke)
--    into the trust directory. The status vocabulary:
--      ACTIVE   — current signer; verifies QRs.
--      SUSPENDED — temporarily untrusted; fail closed (KEY_SUSPENDED).
--      RETIRED  — superseded by a newer version; verifies historical QRs
--                  that the ACTIVE key signature no longer matches (Phase 4
--                  fallback path).
--      REVOKED  — compromised or explicitly distrusted; fail closed
--                  (KEY_REVOKED), must never verify.
--
--    KeyCustody-internal intermediates (GENERATING, PENDING, RETIRING,
--    SUSPENDING) never reach this table.
-- ---------------------------------------------------------------------------

-- Table-existence guard (fresh-chain repair, 2026-09-12): the institution_
-- public.institution_keys table is created by 005_institution_trust.sql,
-- which runs AFTER this script in the ordinal chain. DROP CONSTRAINT IF
-- EXISTS tolerates a missing CONSTRAINT but not a missing TABLE, so on a
-- fresh database this block raised 42P01 and broke the whole chain (only
-- environments carrying a pre-consolidation table ever got past it — no CI
-- ran the chain fresh, which is how this survived). Guarded now: fresh
-- chains skip this block and 005's CREATE TABLE carries the FINAL status
-- vocabulary including 'REVOKED' directly; chains that already hold the
-- table (re-runs against an existing schema) apply it as before.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_tables
        WHERE schemaname = 'public' AND tablename = 'institution_keys')
    THEN
        ALTER TABLE public.institution_keys
            DROP CONSTRAINT IF EXISTS ck_institution_keys_status;

        ALTER TABLE public.institution_keys
            ADD CONSTRAINT ck_institution_keys_status
            CHECK (status IN ('ACTIVE', 'SUSPENDED', 'RETIRED', 'REVOKED'));

        CREATE INDEX IF NOT EXISTS ix_institution_keys_code_retired
            ON public.institution_keys (institution_code)
            WHERE status = 'RETIRED' AND is_active = TRUE;

        COMMENT ON COLUMN public.institution_keys.status IS
            'Trust-directory status vocabulary (post-consolidation): ACTIVE (current '
            'signer), SUSPENDED (temporarily untrusted — fail closed), '
            'RETIRED (superseded by a newer version — verifies historical QRs), '
            'REVOKED (compromised — fail closed, must never verify). '
            'Maps from KeyCustody lifecycle transitions; KeyCustody-internal states '
            '(GENERATING, PENDING, SUSPENDING) never reach this table.';
    END IF;
END
$$;

COMMIT;
