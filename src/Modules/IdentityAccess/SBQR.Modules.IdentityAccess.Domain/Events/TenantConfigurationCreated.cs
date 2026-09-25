using SBQR.Modules.IdentityAccess.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.IdentityAccess.Domain.Events;

/// <summary>
/// Raised by <see cref="TenantConfiguration.Register"/> when a brand-new
/// configuration row has been minted for a tenant. Subscribers (Audit module)
/// record that a <c>tenant_configurations</c> row was written; the plaintext
/// secret is <b>not</b> carried in this event — it was returned to the caller
/// once via the response DTO and is never persisted anywhere else.
///
/// <para>
/// The event also carries the initial capability flags so the audit-log row
/// captures the as-of-issuance QR privileges alongside the credential.
/// </para>
///
/// <para>The tenant id is an opaque <see cref="Guid"/> here: the strongly-typed
/// <c>TenantId</c> value object belongs to the Tenancy bounded context and must
/// not leak across the module boundary.</para>
/// </summary>
public sealed record TenantConfigurationCreated(
    Guid TenantId,
    TenantConfigurationId TenantConfigurationId,
    string ClientId,
    DateTimeOffset? ExpiresAt,
    bool IsQrGenerationAllowed,
    bool IsQrValidationAllowed,
    DateTimeOffset OccurredAt) : IDomainEvent;
