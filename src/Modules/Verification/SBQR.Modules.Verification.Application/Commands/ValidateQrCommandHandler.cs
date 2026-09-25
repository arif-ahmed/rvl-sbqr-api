using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.Verification.Domain.Aggregates;
using SBQR.Modules.Verification.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Cryptography;
using SBQR.SharedKernel.QrCodec;

namespace SBQR.Modules.Verification.Application.Commands;

/// <summary>
/// The verification pipeline (Annex B):
///
///   replay window check (C6)
///   → parse + CRC (structural gate)
///   → classify (NON_P2P vs P2P)
///   → resolve the issuer's trusted key from the single BB trust store
///     (InstitutionTrust) — <b>no own-custody branch exists</b>; tenant keys
///     are published into the same store by KeyCustody (C16)
///   → reconstruct the signature payload (same codec function QrGeneration
///     signs with)
///   → Ed25519 verify
///   → if signature fails, retry against the latest RETIRED key (historical
///     key resolution — Phase 4)
///   → persist the outcome in one save
///
/// Every failure path still records a row: fail-closed with evidence (A1, A5).
///
/// The (verifying tenant, request id) pair is single-use (C6): the DB unique
/// constraint <c>uq_qr_validations_replay</c> turns a replayed request into a
/// 23505 that is converted into a REQUEST_REPLAYED response — the rejection
/// is audited (<c>qr.validation.rejected</c>), never re-recorded here.
/// <c>qr_validations.tenant_id</c> is the VERIFYING tenant (C5), resolved
/// from the authenticated credential via <see cref="ICurrentTenant"/> —
/// never from the request body.
///
/// Spec Annex B step 2: "retrieve the corresponding public key file from
/// the BB Trust Store using the derived Institution_ID" — this handler is
/// the in-process enforcement of that: every issuer, tenant-owned or
/// external, resolves through <see cref="GetInstitutionPublicKeyQuery"/>.
/// </summary>
public sealed class ValidateQrCommandHandler : IRequestHandler<ValidateQrCommand, ValidateQrResult>
{
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(5);

    private readonly ISender _mediator;
    private readonly ISignatureVerifier _verifier;
    private readonly VerificationDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly ICurrentTenant _currentTenant;

    public ValidateQrCommandHandler(
        ISender mediator,
        ISignatureVerifier verifier,
        VerificationDbContext db,
        IAuditLogger audit,
        ICurrentTenant currentTenant)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _currentTenant = currentTenant ?? throw new ArgumentNullException(nameof(currentTenant));
    }

    public async Task<ValidateQrResult> Handle(ValidateQrCommand request, CancellationToken cancellationToken)
    {
        var verifyingTenantId = _currentTenant.TenantId;
        if (verifyingTenantId == Guid.Empty)
        {
            // Fail-closed (C15/C5): the verify endpoint is tenant-scoped; an
            // unauthenticated call must never be recorded as any tenant's
            // validation. The authorize policy should have rejected earlier.
            throw new InvalidOperationException(
                "QR validation requires an authenticated tenant context (ICurrentTenant.TenantId).");
        }

        var payloadHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(request.QrPayload)))
            .ToLowerInvariant();

        // 0. Replay window (C6) — defense-in-depth: the FluentValidation rule
        //    already rejects stale timestamps with 400; this gate keeps the
        //    invariant even when a caller bypasses the validation pipeline.
        if (Math.Abs((DateTimeOffset.UtcNow - request.RequestTimestamp).TotalMinutes) > ReplayWindow.TotalMinutes)
        {
            return await RecordAsync(
                new ValidateQrResult(
                    QrVerdict.REQUEST_STALE,
                    TrustSource: null,
                    ReasonCode: "TIMESTAMP_STALE",
                    InstitutionCode: null,
                    PayloadHash: payloadHash,
                    RecipientName: null,
                    RecipientPan: null,
                    QrClassification: "UNKNOWN"),
                tenantId: null,
                request,
                verifyingTenantId,
                cancellationToken).ConfigureAwait(false);
        }

        // 1. Structural gate — CRC first, mandatory tags, Tag 26 shape.
        var parse = QrPayloadParser.Parse(request.QrPayload);
        if (!parse.IsSuccess)
        {
            var reason = parse.Errors[0].Code.StableCode();
            return await RecordAsync(
                new ValidateQrResult(
                    QrVerdict.STRUCTURAL_INVALID,
                    TrustSource: null,
                    ReasonCode: reason,
                    InstitutionCode: null,
                    PayloadHash: payloadHash,
                    RecipientName: null,
                    RecipientPan: null,
                    QrClassification: "UNKNOWN"),
                tenantId: null,
                request,
                verifyingTenantId,
                cancellationToken).ConfigureAwait(false);
        }

        var payload = parse.Payload;

        // Spec Annex B step 2: Institution_ID = Tag 26 Sub-tag 01 ‖ Sub-tag 02.
        // Built via the InstitutionId value object so the construction is
        // spec-compliant and unit-testable in one place.
        var institutionId = payload.ResolvedInstitutionId;
        if (institutionId is null)
        {
            return await RecordAsync(
                new ValidateQrResult(
                    QrVerdict.STRUCTURAL_INVALID,
                    TrustSource: null,
                    ReasonCode: "INSTITUTION_ID_MISSING",
                    InstitutionCode: null,
                    PayloadHash: payloadHash,
                    RecipientName: payload.RecipientName,
                    RecipientPan: payload.RecipientPan,
                    QrClassification: "P2P"),
                tenantId: null,
                request,
                verifyingTenantId,
                cancellationToken).ConfigureAwait(false);
        }

        var institutionCode = institutionId.Value;

        // 2. Classification — NON_P2P is a valid outcome, not an error.
        if (payload.Classification == QrClassification.NonP2P)
        {
            return await RecordAsync(
                new ValidateQrResult(
                    QrVerdict.NON_P2P,
                    TrustSource: null,
                    ReasonCode: null,
                    InstitutionCode: institutionCode,
                    PayloadHash: payloadHash,
                    RecipientName: payload.RecipientName,
                    RecipientPan: payload.RecipientPan,
                    QrClassification: "NON_P2P"),
                tenantId: null,
                request,
                verifyingTenantId,
                cancellationToken).ConfigureAwait(false);
        }

        // 3. Key resolution — trust store only (spec Annex B; C16).
        //    No signature tags means the QR is unsigned: rejected.
        var signatureBase64 = payload.SignatureBase64;
        if (signatureBase64 is null)
        {
            return await RecordAsync(
                new ValidateQrResult(
                    QrVerdict.INVALID_SIGNATURE,
                    TrustSource: null,
                    ReasonCode: "SIGNATURE_TAGS_MISSING",
                    InstitutionCode: institutionCode,
                    PayloadHash: payloadHash,
                    RecipientName: payload.RecipientName,
                    RecipientPan: payload.RecipientPan,
                    QrClassification: "P2P"),
                tenantId: null,
                request,
                verifyingTenantId,
                cancellationToken).ConfigureAwait(false);
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            signatureBytes = Array.Empty<byte>();
        }

        if (signatureBytes.Length != 64)
        {
            return await RecordAsync(
                new ValidateQrResult(
                    QrVerdict.INVALID_SIGNATURE,
                    TrustSource: null,
                    ReasonCode: "SIGNATURE_MALFORMED",
                    InstitutionCode: institutionCode,
                    PayloadHash: payloadHash,
                    RecipientName: payload.RecipientName,
                    RecipientPan: payload.RecipientPan,
                    QrClassification: "P2P"),
                tenantId: null,
                request,
                verifyingTenantId,
                cancellationToken).ConfigureAwait(false);
        }

        // 4. Resolve the issuer's key from the BB trust store (Annex B step 2).
        //    Single authority — no own-custody branch.
        var directoryKey = await _mediator
            .Send(new GetInstitutionPublicKeyQuery(institutionCode), cancellationToken)
            .ConfigureAwait(false);

        if (directoryKey is null)
        {
            return await RecordAsync(
                new ValidateQrResult(
                    QrVerdict.KEY_NOT_FOUND,
                    TrustSource: null,
                    ReasonCode: "TRUST_DIRECTORY_MISS",
                    InstitutionCode: institutionCode,
                    PayloadHash: payloadHash,
                    RecipientName: payload.RecipientName,
                    RecipientPan: payload.RecipientPan,
                    QrClassification: "P2P"),
                tenantId: null,
                request,
                verifyingTenantId,
                cancellationToken).ConfigureAwait(false);
        }

        // Status gate (C16): the trust store's status vocabulary carries the
        // full KeyCustody lifecycle. Map non-ACTIVE statuses to fail-closed
        // verdicts before attempting signature verification.
        var status = directoryKey.Status;
        switch (status)
        {
            case "SUSPENDED":
                return await RejectionAsync(
                    QrVerdict.KEY_SUSPENDED, "KEY_SUSPENDED", institutionCode, payload, payloadHash, verifyingTenantId,
                    request, cancellationToken).ConfigureAwait(false);

            case "REVOKED":
                return await RejectionAsync(
                    QrVerdict.KEY_REVOKED, "KEY_REVOKED", institutionCode, payload, payloadHash, verifyingTenantId,
                    request, cancellationToken).ConfigureAwait(false);

            case "RETIRED":
                // A RETIRED key is returned by the ACTIVE query only if
                // IncludeHistorical was set — which it wasn't on the primary
                // query. If it somehow appears here (race during rotation),
                // treat it as KEY_NOT_ACTIVE: the active key slot is empty.
                return await RejectionAsync(
                    QrVerdict.KEY_NOT_ACTIVE, "KEY_RETIRED_ACTIVE_QUERY", institutionCode, payload, payloadHash,
                    verifyingTenantId, request, cancellationToken).ConfigureAwait(false);
        }

        // 5. Verify — the SAME signature-payload reconstruction QrGeneration signed with.
        var signaturePayload = payload.GetSignaturePayloadBytes();
        var verified = await _verifier
            .VerifyAsync(signaturePayload, signatureBytes, Encoding.ASCII.GetBytes(directoryKey.PublicKeyPem), cancellationToken)
            .ConfigureAwait(false);

        if (verified)
        {
            return await RecordAsync(
                new ValidateQrResult(
                    QrVerdict.VALID,
                    QrTrustSource.TRUST_DIRECTORY,
                    ReasonCode: null,
                    InstitutionCode: institutionCode,
                    PayloadHash: payloadHash,
                    RecipientName: payload.RecipientName,
                    RecipientPan: payload.RecipientPan,
                    QrClassification: "P2P"),
                tenantId: null,
                request,
                verifyingTenantId,
                cancellationToken).ConfigureAwait(false);
        }

        // 6. Historical key resolution (Phase 4): signature failed against
        //    the current ACTIVE key. Retry against the latest RETIRED key —
        //    a QR issued before the most recent rotation would fail here.
        //    REVOKED keys are never retried (fail-closed on compromise).
        var historicalKey = await _mediator
            .Send(new GetInstitutionPublicKeyQuery(institutionCode, IncludeHistorical: true), cancellationToken)
            .ConfigureAwait(false);

        if (historicalKey is not null && historicalKey.Status is "RETIRED")
        {
            var historicalVerified = await _verifier
                .VerifyAsync(signaturePayload, signatureBytes,
                    Encoding.ASCII.GetBytes(historicalKey.PublicKeyPem), cancellationToken)
                .ConfigureAwait(false);

            if (historicalVerified)
            {
                // Valid against a historical key — record with a note so
                // operators can see this QR was signed pre-rotation.
                return await RecordAsync(
                    new ValidateQrResult(
                        QrVerdict.VALID,
                        QrTrustSource.TRUST_DIRECTORY,
                        ReasonCode: "VERIFIED_HISTORICAL_KEY",
                        InstitutionCode: institutionCode,
                        PayloadHash: payloadHash,
                        RecipientName: payload.RecipientName,
                        RecipientPan: payload.RecipientPan,
                        QrClassification: "P2P"),
                    tenantId: null,
                    request,
                    verifyingTenantId,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        // Neither the active nor the historical key verifies.
        return await RecordAsync(
            new ValidateQrResult(
                QrVerdict.INVALID_SIGNATURE,
                QrTrustSource.TRUST_DIRECTORY,
                ReasonCode: "SIGNATURE_MISMATCH",
                InstitutionCode: institutionCode,
                PayloadHash: payloadHash,
                RecipientName: payload.RecipientName,
                RecipientPan: payload.RecipientPan,
                QrClassification: "P2P"),
            tenantId: null,
            request,
            verifyingTenantId,
            cancellationToken).ConfigureAwait(false);

        // --- Local helpers ---

        async Task<ValidateQrResult> RejectionAsync(
            QrVerdict verdict,
            string reasonCode,
            string institutionCode,
            ParsedQrPayload payload,
            string payloadHash,
            Guid verifyingTenantId,
            ValidateQrCommand request,
            CancellationToken ct)
        {
            return await RecordAsync(
                new ValidateQrResult(
                    verdict,
                    TrustSource: null,
                    ReasonCode: reasonCode,
                    InstitutionCode: institutionCode,
                    PayloadHash: payloadHash,
                    RecipientName: payload.RecipientName,
                    RecipientPan: payload.RecipientPan,
                    QrClassification: "P2P"),
                tenantId: null,
                request,
                verifyingTenantId,
                ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Persists exactly one <c>qr_validations</c> row (A5) and audits the
    /// verdict. A duplicate (tenant, request id) — a replayed request —
    /// surfaces as 23505 from the unique index; it is converted into a
    /// REQUEST_REPLAYED response whose rejection is audited
    /// (qr.validation.rejected), not re-recorded here.
    /// </summary>
    private async Task<ValidateQrResult> RecordAsync(
        ValidateQrResult result,
        Guid? tenantId,
        ValidateQrCommand request,
        Guid verifyingTenantId,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();

        var row = new QrValidation
        {
            QrValidationId = Guid.NewGuid(),
            TenantId = verifyingTenantId,
            RequestId = request.RequestId,
            RequestTimestamp = request.RequestTimestamp,
            CorrelationId = correlationId,
            InstitutionCode = result.InstitutionCode,
            Verdict = result.Verdict.ToString(),
            TrustSource = result.TrustSource?.ToString() ?? "NONE",
            ReasonCode = result.ReasonCode,
            CreatedBy = $"tenant:{verifyingTenantId}",
        };
        _db.Validations.Add(row);

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // C6 replay guard: this (tenant, request id) was already used.
            // The row is NOT re-recorded — the audit line below is the
            // evidence of the rejection (A5).
            return await AuditRejectionAsync(
                QrVerdict.REQUEST_REPLAYED, "REPLAY_DETECTED", correlationId,
                result, tenantId, verifyingTenantId, cancellationToken).ConfigureAwait(false);
        }

        await _audit.LogAsync(new AuditEntry(
            Action: "qr.validated",
            ActorId: $"tenant:{verifyingTenantId}",
            ResourceType: "qr_validation",
            ResourceId: result.PayloadHash,
            Metadata: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["correlation_id"] = correlationId,
                ["verdict"] = result.Verdict.ToString(),
                ["trust_source"] = result.TrustSource?.ToString(),
                ["institution_code"] = result.InstitutionCode,
                ["reason_code"] = result.ReasonCode,
                ["issuing_tenant_id"] = tenantId,
            }),
            TenantId: verifyingTenantId),
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>Audits a rejected-before-recorded request (replay) with its own correlation id.</summary>
    private async Task<ValidateQrResult> AuditRejectionAsync(
        QrVerdict verdict,
        string reasonCode,
        Guid correlationId,
        ValidateQrResult result,
        Guid? issuingTenantId,
        Guid verifyingTenantId,
        CancellationToken cancellationToken)
    {
        var rejection = new ValidateQrResult(
            verdict,
            TrustSource: null,
            ReasonCode: reasonCode,
            InstitutionCode: result.InstitutionCode,
            PayloadHash: result.PayloadHash,
            RecipientName: result.RecipientName,
            RecipientPan: result.RecipientPan,
            QrClassification: result.QrClassification);

        await _audit.LogAsync(new AuditEntry(
            Action: "qr.validation.rejected",
            ActorId: $"tenant:{verifyingTenantId}",
            ResourceType: "qr_validation",
            ResourceId: result.PayloadHash,
            Metadata: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["correlation_id"] = correlationId,
                ["verdict"] = verdict.ToString(),
                ["reason_code"] = reasonCode,
                ["institution_code"] = result.InstitutionCode,
                ["issuing_tenant_id"] = issuingTenantId,
            }),
            TenantId: verifyingTenantId),
            cancellationToken).ConfigureAwait(false);

        return rejection;
    }
}
