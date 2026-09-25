using MediatR;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.KeyCustody.Domain.Interfaces;
using SBQR.Modules.Tenancy.Contracts;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.QrCodec;

namespace SBQR.Modules.KeyCustody.Application.Commands.ReinstateTenantSigningKeys;

/// <summary>
/// Handles the cross-module <see cref="ReinstateTenantSigningKeysCommand"/>
/// cascade fired by Tenancy's Reactivate handler. Tolerant: if the tenant has
/// no SUSPENDED key, this is a silent no-op.
///
/// <para>
/// <b>Trust-store propagation</b> (Phase 3b): after reinstating the
/// custodial key, the handler restores the corresponding InstitutionTrust
/// row to ACTIVE (via
/// <see cref="IInstitutionTrustPublisher.UpdateKeyStatusAsync"/>) so
/// Verification resumes using it as a trusted signer.
/// </para>
/// </summary>
public sealed class ReinstateTenantSigningKeysCommandHandler
    : IRequestHandler<ReinstateTenantSigningKeysCommand>
{
    private readonly ITenantDirectory _tenants;
    private readonly ICryptoKeyRepository _keys;
    private readonly IKeyCustodyUnitOfWork _uow;
    private readonly IInstitutionTrustPublisher _trust;
    private readonly IAuditLogger _audit;
    private readonly IActorProvider _actor;

    public ReinstateTenantSigningKeysCommandHandler(
        ITenantDirectory tenants,
        ICryptoKeyRepository keys,
        IKeyCustodyUnitOfWork uow,
        IInstitutionTrustPublisher trust,
        IAuditLogger audit,
        IActorProvider actor)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
    }

    public async Task Handle(ReinstateTenantSigningKeysCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var suspendedKey = await _keys
            .GetSuspendedByTenantAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (suspendedKey is null)
        {
            return;
        }

        suspendedKey.Reinstate();
        await _keys.UpdateAsync(suspendedKey, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Propagate the ACTIVE status back to the trust store so Verification
        // resumes using this key for signature verification.
        // Best-effort: if the trust store is unreachable, the KeyCustody
        // row is already ACTIVE — the audit log captures the trust-store
        // failure for operator follow-up.
        try
        {
            var tenant = await _tenants
                .LookupAsync(request.TenantId, cancellationToken)
                .ConfigureAwait(false);
            if (tenant is not null)
            {
                var institutionId = InstitutionId.FromCode(tenant.InstitutionCode);
                await _trust.UpdateKeyStatusAsync(
                    institutionCode: institutionId.Value,
                    newStatus: "ACTIVE",
                    actorId: IInstitutionTrustPublisher.CryptoCreateActor,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await _audit.LogAsync(
                new AuditEntry(
                    Action: "crypto_key.trust_reinstate_failed",
                    ActorId: _actor.CurrentActor(),
                    ResourceType: "CryptoKey",
                    ResourceId: suspendedKey.Id.Value.ToString(),
                    Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"key_id\":\"{suspendedKey.KeyId}\",\"key_version\":{suspendedKey.KeyVersion},\"reason\":\"{ex.GetType().Name}: {ex.Message}\"}}",
                    TenantId: request.TenantId),
                cancellationToken).ConfigureAwait(false);
        }

        await _audit.LogAsync(
            new AuditEntry(
                Action: "crypto_key.reinstated",
                ActorId: _actor.CurrentActor(),
                ResourceType: "CryptoKey",
                ResourceId: suspendedKey.Id.Value.ToString(),
                Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"key_id\":\"{suspendedKey.KeyId}\",\"key_version\":{suspendedKey.KeyVersion}}}"),
            cancellationToken).ConfigureAwait(false);
    }
}
