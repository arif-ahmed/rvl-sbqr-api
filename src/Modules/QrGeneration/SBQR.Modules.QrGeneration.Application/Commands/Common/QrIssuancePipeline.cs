using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.QrGeneration.Infrastructure.Persistence;
using SBQR.Modules.Tenancy.Contracts;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Cryptography;
using SBQR.SharedKernel.QrCodec;

namespace SBQR.Modules.QrGeneration.Application.Commands.Common;

/// <summary>
/// The canonical issuance pipeline (PRD §5.3): resolve tenant identity →
/// enforce admission gate (active tenants only) → enforce the activation
/// gate (only an ACTIVE custodied key may sign) → build the unsigned
/// canonical payload via the codec → sign the signature payload through
/// KeyCustody's provider → finalize (Tags 80/81 inserted, CRC computed
/// LAST) → persist the generation row (no payload bytes, no payload hash
/// — decision 2026-09-08) → audit. The QR string is returned to the
/// caller exactly once and is never persisted.
///
/// Both <see cref="GenerateStaticQr.IssueStaticQrService"/> and
/// <see cref="GenerateDynamicQr.IssueDynamicQrService"/> call this single
/// pipeline — no second MediatR round-trip, no second implementation of the
/// audit envelope, no second activation-gate check.
///
/// Fail-closed outer <c>catch</c> (A12) wraps the whole method body so any
/// unexpected exception (dependency outage, unhandled DB error, bug)
/// becomes a generic 500 with a <c>correlation_id</c> the caller and ops
/// can join on; internal-state and exception detail are logged only —
/// never returned to the client (C13).
/// </summary>
public sealed partial class QrIssuancePipeline
{
    private readonly ISender _mediator;
    private readonly ISigningProvider _signingProvider;
    private readonly ITenantDirectory _tenants;
    private readonly ITenantAdmissionDirectory _admission;
    private readonly QrGenerationDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<QrIssuancePipeline> _logger;

    public QrIssuancePipeline(
        ISender mediator,
        ISigningProvider signingProvider,
        ITenantDirectory tenants,
        ITenantAdmissionDirectory admission,
        QrGenerationDbContext db,
        IAuditLogger audit,
        ICurrentTenant currentTenant,
        ILogger<QrIssuancePipeline> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _signingProvider = signingProvider ?? throw new ArgumentNullException(nameof(signingProvider));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _currentTenant = currentTenant ?? throw new ArgumentNullException(nameof(currentTenant));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// One issuance. The pipeline is the only place where tenant lookup,
    /// admission gating, key gating, signing, persistence, and audit happen;
    /// per-type issuance services are responsible only for assembling the
    /// recipient-identity fields into the codec request.
    /// </summary>
    public async Task<Result<GenerateQrResult>> IssueAsync(
        QrType qrType,
        string recipientName,
        string recipientCity,
        string recipientPan,
        string? transactionAmount,
        string? postalCode,
        string? customerLabel,
        string? purposeOfTransaction,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Every rejection and failure below is audited under this id (A5,
        // A11) so ops can pull the exact request from logs and the audit
        // trail with one value, whether the request was rejected, the
        // signer failed, or something unexpected happened.
        var correlationId = Guid.NewGuid();
        var qrTypeWire = qrType.ToWireValue();
        var tenantId = _currentTenant.TenantId;

        if (tenantId == Guid.Empty)
        {
            return Result<GenerateQrResult>.Failure(
                ErrorCode.Unauthenticated, "QR generation requires an authenticated tenant request.");
        }

        var tenant = await _tenants.LookupAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenant?.InstitutionCode is not { Length: 6 } institutionCode)
        {
            // Deliberately not 401: an authenticated request whose tenant
            // record cannot be resolved collapses to the same response as
            // a missing token. The caller never learns whether the
            // institution code was wrong, missing, or the tenant has been
            // terminated.
            return Result<GenerateQrResult>.Failure(
                ErrorCode.Unauthenticated,
                $"Tenant is not permitted to issue QRs. reference={correlationId}");
        }

        var admission = await _admission.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (admission != TenantAdmissionState.Active)
        {
            LogTenantNotActive(_logger, correlationId, tenantId, institutionCode, admission?.ToString() ?? "NONE", idempotencyKey);
            await AuditRejectionAsync(
                correlationId, tenantId, institutionCode, qrTypeWire, "TENANT_NOT_ACTIVE", idempotencyKey,
                cancellationToken).ConfigureAwait(false);
            return Result<GenerateQrResult>.Failure(
                ErrorCode.Forbidden, $"Tenant is not permitted to issue QRs. reference={correlationId}");
        }

        var identity = new InstitutionIdentity(
            InstitutionCode: institutionCode,
            InstitutionType: institutionCode[..2],
            InstitutionId: institutionCode[2..]);

        try
        {
            // Activation gate: only an ACTIVE custodied key may sign. This is
            // the Customer/Supplier boundary — the query never exposes private
            // material, and there is no fallback path. Internal state (key
            // status, institution code) is logged only — never returned to the
            // client (C13).
            var key = await _mediator.Send(new GetSigningKeyQuery(tenantId), cancellationToken)
                .ConfigureAwait(false);
            if (key is null || key.Status != "ACTIVE")
            {
                LogKeyNotActive(_logger, correlationId, tenantId, institutionCode, key?.Status ?? "NONE", idempotencyKey);
                await AuditRejectionAsync(
                    correlationId, tenantId, institutionCode, qrTypeWire, "KEY_NOT_ACTIVE", idempotencyKey,
                    cancellationToken).ConfigureAwait(false);
                return Result<GenerateQrResult>.Failure(
                    ErrorCode.InvalidState, $"Tenant has no active signing key. reference={correlationId}");
            }

            var build = P2pQrBuilder.Build(new P2pQrRequest(
                IsDynamic: qrType == QrType.Dynamic,
                InstitutionType: identity.InstitutionType,
                InstitutionId: identity.InstitutionId,
                RecipientPan: recipientPan.Trim(),
                RecipientName: recipientName.Trim(),
                RecipientCity: recipientCity.Trim(),
                TransactionAmount: transactionAmount,
                PostalCode: postalCode,
                CustomerLabel: customerLabel,
                PurposeOfTransaction: purposeOfTransaction));
            if (!build.IsSuccess)
            {
                await AuditRejectionAsync(
                    correlationId, tenantId, institutionCode, qrTypeWire, "VALIDATION_FAILED", idempotencyKey,
                    cancellationToken).ConfigureAwait(false);
                return Result<GenerateQrResult>.Failure(
                    ErrorCode.ValidationFailed,
                    string.Join("; ", build.Errors.Select(e => e.Message)));
            }

            byte[] signature;
            try
            {
                signature = await _signingProvider
                    .SignAsync(build.Value.SignaturePayloadBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (CryptographicException ex)
            {
                // Distinct SIGNING_FAILED code (ErrorCode.SigningFailed): a
                // key/HSM problem, not the generic 500 an unhandled bug would
                // produce — ops can tell the two apart at a glance.
                LogSigningFailed(_logger, ex, correlationId, tenantId, idempotencyKey);
                await _audit.LogAsync(new AuditEntry(
                    Action: "qr.generation.failed",
                    ActorId: $"tenant:{institutionCode}",
                    ResourceType: "qr_generation",
                    ResourceId: correlationId.ToString(),
                    Metadata: JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["correlation_id"] = correlationId,
                        ["reason_code"] = "SIGNING_FAILED",
                        ["qr_type"] = qrTypeWire,
                        ["institution_code"] = institutionCode,
                        ["key_version"] = key.KeyVersion,
                        ["idempotency_key"] = idempotencyKey,
                    }),
                    TenantId: tenantId),
                    cancellationToken).ConfigureAwait(false);
                return Result<GenerateQrResult>.Failure(
                    ErrorCode.SigningFailed, $"QR signing failed. reference={correlationId}");
            }

            // CRC computed last inside Finalize (assertion L2) — Tags 80/81 are
            // inserted before the checksum by construction.
            var qrPayload = QrPayloadFinalizer.Finalize(
                build.Value,
                Convert.ToBase64String(signature));
            var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(qrPayload)))
                .ToLowerInvariant();

            _db.Generations.Add(new Domain.Aggregates.QrGeneration
            {
                QrGenerationId = Guid.NewGuid(),
                TenantId = tenantId,
                QrType = qrTypeWire,
                SignatureKeyVersion = key.KeyVersion,
                IdempotencyKey = idempotencyKey,
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                // Idempotency (L12): tenant-scoped duplicate key — reject, never
                // replay (the payload is not stored, so replay is impossible).
                await AuditRejectionAsync(
                    correlationId, tenantId, institutionCode, qrTypeWire, "DUPLICATE_IDEMPOTENCY_KEY", idempotencyKey,
                    cancellationToken).ConfigureAwait(false);
                return Result<GenerateQrResult>.Failure(
                    ErrorCode.Conflict, "This idempotency key was already used by this tenant.");
            }

            // Happy-path breadcrumb (EventId 7004). Carries the client-supplied
            // idempotency_key when present so ops can grep logs for a specific
            // key — useful when a tenant reports "I sent X but the system
            // says duplicate" and we need to find both legs.
            LogIssuanceAccepted(_logger, correlationId, tenantId, institutionCode, qrTypeWire, key.KeyVersion, idempotencyKey);

            await _audit.LogAsync(new AuditEntry(
                Action: "qr.generated",
                ActorId: $"tenant:{tenant.InstitutionCode}",
                ResourceType: "qr_generation",
                ResourceId: payloadHash,
                Metadata: JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["correlation_id"] = correlationId,
                    ["qr_type"] = qrTypeWire,
                    ["key_version"] = key.KeyVersion,
                    ["institution_code"] = institutionCode,
                    ["idempotency_key"] = idempotencyKey,
                }),
                TenantId: tenantId),
                cancellationToken).ConfigureAwait(false);

            return new GenerateQrResult(
                QrPayload: qrPayload,
                PayloadHash: payloadHash,
                SignatureKeyVersion: key.KeyVersion,
                QrType: qrTypeWire);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail closed (A12) on anything unexpected below this point — a
            // dependency outage, an unhandled DB error, a bug. The response
            // never carries the exception detail (C13); the correlation id
            // is the caller's and ops' shared handle back to this exact
            // failure in both the log line and the audit row (A5, A11).
            LogGenerationFailed(_logger, ex, correlationId, tenantId, idempotencyKey);
            await _audit.LogAsync(new AuditEntry(
                Action: "qr.generation.failed",
                ActorId: $"tenant:{institutionCode}",
                ResourceType: "qr_generation",
                ResourceId: correlationId.ToString(),
                Metadata: JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["correlation_id"] = correlationId,
                    ["reason_code"] = "UNEXPECTED_ERROR",
                    ["qr_type"] = qrTypeWire,
                    ["institution_code"] = institutionCode,
                    ["idempotency_key"] = idempotencyKey,
                }),
                TenantId: tenantId),
                cancellationToken).ConfigureAwait(false);
            return Result<GenerateQrResult>.Failure(
                ErrorCode.Unspecified, $"QR generation failed unexpectedly. reference={correlationId}");
        }
    }

    /// <summary>
    /// Audits an expected rejection (bad admission state, bad key state,
    /// failed validation, or a duplicate idempotency key) under the
    /// request's correlation id — every rejection leaves a traceable audit
    /// line, not only the successful path.
    /// </summary>
    private Task AuditRejectionAsync(
        Guid correlationId,
        Guid tenantId,
        string institutionCode,
        string qrTypeWire,
        string reasonCode,
        string? idempotencyKey,
        CancellationToken cancellationToken) =>
        _audit.LogAsync(new AuditEntry(
            Action: "qr.generation.rejected",
            ActorId: $"tenant:{institutionCode}",
            ResourceType: "qr_generation",
            ResourceId: correlationId.ToString(),
            Metadata: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["correlation_id"] = correlationId,
                ["reason_code"] = reasonCode,
                ["qr_type"] = qrTypeWire,
                ["institution_code"] = institutionCode,
                ["idempotency_key"] = idempotencyKey,
            }),
            TenantId: tenantId),
            cancellationToken);

    [LoggerMessage(
        EventId = 7000,
        Level = LogLevel.Warning,
        Message = "QR generation rejected [{CorrelationId}]: tenant {TenantId} (institution {InstitutionCode}) admission state is '{AdmissionState}', expected Active. idempotency_key={IdempotencyKey}")]
    private static partial void LogTenantNotActive(
        ILogger logger, Guid correlationId, Guid tenantId, string institutionCode, string admissionState, string? idempotencyKey);

    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Warning,
        Message = "QR generation rejected [{CorrelationId}]: tenant {TenantId} (institution {InstitutionCode}) signing key status is '{KeyStatus}', expected ACTIVE. idempotency_key={IdempotencyKey}")]
    private static partial void LogKeyNotActive(
        ILogger logger, Guid correlationId, Guid tenantId, string institutionCode, string keyStatus, string? idempotencyKey);

    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Error,
        Message = "QR signing failed [{CorrelationId}] for tenant {TenantId}. idempotency_key={IdempotencyKey}")]
    private static partial void LogSigningFailed(
        ILogger logger, Exception ex, Guid correlationId, Guid tenantId, string? idempotencyKey);

    [LoggerMessage(
        EventId = 7003,
        Level = LogLevel.Error,
        Message = "QR generation failed unexpectedly [{CorrelationId}] for tenant {TenantId}. idempotency_key={IdempotencyKey}")]
    private static partial void LogGenerationFailed(
        ILogger logger, Exception ex, Guid correlationId, Guid tenantId, string? idempotencyKey);

    [LoggerMessage(
        EventId = 7004,
        Level = LogLevel.Information,
        Message = "QR issuance accepted [{CorrelationId}]: tenant {TenantId} (institution {InstitutionCode}) qr_type={QrType} key_version={KeyVersion} idempotency_key={IdempotencyKey}")]
    private static partial void LogIssuanceAccepted(
        ILogger logger, Guid correlationId, Guid tenantId, string institutionCode, string qrType, int keyVersion, string? idempotencyKey);
}
