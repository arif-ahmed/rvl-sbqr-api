using MediatR;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.Modules.KeyCustody.Domain.Interfaces;
using SBQR.Modules.Tenancy.Contracts;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.QrCodec;

namespace SBQR.Modules.KeyCustody.Application.Commands.SuspendTenantSigningKeys;

/// <summary>
/// Handles the cross-module <see cref="SuspendTenantSigningKeysCommand"/>
/// cascade fired by Tenancy's Suspend/Terminate handlers. Tolerant: if the
/// tenant has no ACTIVE key, this is a silent no-op — the cascade only acts
/// on what is present.
///
/// <para>
/// <b>Trust-store propagation</b> (Phase 3b): after suspending the
/// custodial key, the handler marks the corresponding InstitutionTrust row
/// SUSPENDED (via <see cref="IInstitutionTrustPublisher.UpdateKeyStatusAsync"/>)
/// so Verification produces a KEY_SUSPENDED verdict rather than
/// attempting signature verification against a compromised key.
/// </para>
/// </summary>
public sealed class SuspendTenantSigningKeysCommandHandler
    : IRequestHandler<SuspendTenantSigningKeysCommand>
{
    private readonly ITenantDirectory _tenants;
    private readonly ICryptoKeyRepository _keys;
    private readonly IKeyCustodyUnitOfWork _uow;
    private readonly IInstitutionTrustPublisher _trust;
    private readonly IAuditLogger _audit;
    private readonly IActorProvider _actor;

    public SuspendTenantSigningKeysCommandHandler(
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

    public async Task Handle(SuspendTenantSigningKeysCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var activeKey = await _keys
            .GetActiveByTenantAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (activeKey is null)
        {
            return;
        }

        activeKey.Suspend();
        await _keys.UpdateAsync(activeKey, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Propagate the SUSPENDED status to the trust store so Verification
        // (which reads only InstitutionTrust) sees the same lifecycle state.
        // Best-effort: if the trust store is unreachable, the KeyCustody
        // row is already SUSPENDED — the audit log captures the trust-store
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
                    newStatus: "SUSPENDED",
                    actorId: IInstitutionTrustPublisher.CryptoCreateActor,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await _audit.LogAsync(
                new AuditEntry(
                    Action: "crypto_key.trust_suspend_failed",
                    ActorId: _actor.CurrentActor(),
                    ResourceType: "CryptoKey",
                    ResourceId: activeKey.Id.Value.ToString(),
                    Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"key_id\":\"{activeKey.KeyId}\",\"key_version\":{activeKey.KeyVersion},\"reason\":\"{ex.GetType().Name}: {ex.Message}\"}}",
                    TenantId: request.TenantId),
                cancellationToken).ConfigureAwait(false);
        }

        await _audit.LogAsync(
            new AuditEntry(
                Action: "crypto_key.suspended",
                ActorId: _actor.CurrentActor(),
                ResourceType: "CryptoKey",
                ResourceId: activeKey.Id.Value.ToString(),
                Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"key_id\":\"{activeKey.KeyId}\",\"key_version\":{activeKey.KeyVersion}}}"),
            cancellationToken).ConfigureAwait(false);
    }
}
