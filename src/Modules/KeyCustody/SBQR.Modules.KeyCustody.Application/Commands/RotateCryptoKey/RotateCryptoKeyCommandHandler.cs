using MediatR;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.Modules.KeyCustody.Domain.Interfaces;
using SBQR.Modules.Tenancy.Contracts;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Cryptography;

namespace SBQR.Modules.KeyCustody.Application.Commands.RotateCryptoKey;

/// <summary>
/// Handler for <see cref="RotateCryptoKeyCommand"/>: retire the tenant's
/// current ACTIVE key and mint a replacement at <c>oldVersion + 1</c>
/// (server-generated only, no Adopt-on-rotate). Retire + mint + audit happen
/// under a single <see cref="IKeyCustodyUnitOfWork"/> commit; the rotated
/// public key is then auto-published into the InstitutionTrust trust
/// directory (same mandatory-publish contract as mint — see
/// <c>GenerateOrAdoptCryptoKeyCommandHandler</c>) so <c>qr/validate</c>
/// resolves the new version immediately.
/// </summary>
public sealed class RotateCryptoKeyCommandHandler
    : IRequestHandler<RotateCryptoKeyCommand, Result<CryptoKeySummary>>
{
    private readonly ITenantDirectory _tenants;
    private readonly ICryptoKeyRepository _keys;
    private readonly IKeyPairGenerator _generator;
    private readonly ISigningKeyStore _vault;
    private readonly IKeyCustodyUnitOfWork _uow;
    private readonly IInstitutionTrustPublisher _trust;
    private readonly IActorProvider _actor;
    private readonly IAuditLogger _audit;

    public RotateCryptoKeyCommandHandler(
        ITenantDirectory tenants,
        ICryptoKeyRepository keys,
        IKeyPairGenerator generator,
        ISigningKeyStore vault,
        IKeyCustodyUnitOfWork uow,
        IInstitutionTrustPublisher trust,
        IActorProvider actor,
        IAuditLogger audit)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<Result<CryptoKeySummary>> Handle(
        RotateCryptoKeyCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = await _tenants
            .LookupAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<CryptoKeySummary>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId:D} does not exist.");
        }

        var active = await _keys
            .GetActiveByTenantAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (active is null)
        {
            return Result<CryptoKeySummary>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId:D} has no ACTIVE signing key to rotate; use POST to mint one first.");
        }

        active.Retire();
        await _keys.UpdateAsync(active, cancellationToken).ConfigureAwait(false);

        var (publicKeyPem, privateKeyBytes) = _generator.GenerateEd25519();
        var rotated = CryptoKey.Generate(
            tenantId: request.TenantId,
            keyId: active.KeyId,
            institutionCode: tenant.InstitutionCode,
            publicKeyPem: publicKeyPem,
            privateKeyBytes: privateKeyBytes,
            vault: _vault,
            keyVersion: active.KeyVersion + 1);

        await _keys.AddAsync(rotated, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.LogAsync(new AuditEntry(
            Action: "crypto_key.rotated",
            ActorId: _actor.CurrentActor(),
            ResourceType: "CryptoKey",
            ResourceId: rotated.Id.Value.ToString(),
            Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"retired_key_id\":\"{active.Id.Value}\",\"old_version\":{active.KeyVersion},\"new_version\":{rotated.KeyVersion},\"public_key_sha256\":\"{rotated.PublicKeySha256}\"}}",
            TenantId: request.TenantId),
            cancellationToken).ConfigureAwait(false);

        // Mandatory auto-publish: the rotated public key must land in the
        // InstitutionTrust trust directory or Verification will keep
        // resolving the retired key. The crypto_keys rows are preserved on
        // failure — the operator recovers with POST /v1/admin/institutions
        // rather than re-rotating.
        try
        {
            var institutionId = SharedKernel.QrCodec.InstitutionId.FromCode(tenant.InstitutionCode);

            await _trust
                .PublishActivePublicKeyAsync(
                    institutionCode: institutionId.Value,
                    instituteType: institutionId.InstitutionType,
                    institutionName: tenant.InstitutionName,
                    publicKeyPem: rotated.PublicKey,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _audit.LogAsync(new AuditEntry(
                Action: "crypto_key.trust_publish_failed",
                ActorId: _actor.CurrentActor(),
                ResourceType: "CryptoKey",
                ResourceId: rotated.Id.Value.ToString(),
                Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"public_key_sha256\":\"{rotated.PublicKeySha256}\",\"reason\":\"{ex.GetType().Name}: {ex.Message}\"}}",
                TenantId: request.TenantId),
                cancellationToken).ConfigureAwait(false);

            return Result<CryptoKeySummary>.Failure(
                ErrorCode.InvariantViolation,
                $"key rotated but trust-store publish failed ({ex.GetType().Name}: {ex.Message}). " +
                $"Use POST /v1/admin/institutions to register the public key manually.");
        }

        return Result<CryptoKeySummary>.Ok(CryptoKeySummaryBuilder.Build(rotated));
    }
}
