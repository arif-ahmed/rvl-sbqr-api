using System.Security.Cryptography;
using SBQR.Modules.IdentityAccess.Application.Abstractions;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.IdentityAccess.Domain.Aggregates;
using SBQR.Modules.IdentityAccess.Domain.Interfaces;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.IdentityAccess.Application;

/// <summary>
/// Default <see cref="ITenantConfigurationProvisioner"/> — owns the lifecycle of
/// the per-tenant FI client configurations that the OAuth 2.1 client-credentials
/// flow mints. Called by the Tenancy module's provision-tenant-configuration /
/// suspend / reactivate / terminate flows (via the
/// <c>SBQR.Modules.IdentityAccess.Contracts</c> seam) and by nothing else.
///
/// <para>
/// The provisioner is the only writer of the <c>public.tenant_configurations</c>
/// rows — it owns <c>client_id</c> shaping, <c>client_secret</c>
/// generation/hashing, and the cascade-suspend / reinstate state machine.
/// </para>
/// </summary>
public sealed class TenantConfigurationProvisioner : ITenantConfigurationProvisioner
{
    private const int SecretBytes = 32; // 256-bit client_secret (base64url, 43 chars)
    private const int ClientIdHexBytes = 4; // {institution_code}-{8-hex}
    private const int DefaultCredTtlYears = 1;

    private readonly ITenantConfigurationRepository _configurations;
    private readonly IIdentityUnitOfWork _uow;
    private readonly ISecretHasher _hasher;
    private readonly IAuditLogger _audit;

    public TenantConfigurationProvisioner(
        ITenantConfigurationRepository configurations,
        IIdentityUnitOfWork uow,
        ISecretHasher hasher,
        IAuditLogger audit)
    {
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <inheritdoc/>
    public async Task<ProvisionedConfiguration> ProvisionAsync(
        Guid tenantId,
        string institutionCode,
        bool isQrGenerationAllowed,
        bool isQrValidationAllowed,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("tenantId must not be an empty Guid.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(institutionCode);

        // Refuse to issue a second ACTIVE configuration: the rotation story is
        // a separate flow. The partial UQ on tenant_configurations enforces
        // this at the DB level too.
        var existing = await _configurations
            .GetActiveByTenantAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId} already has an active TenantConfiguration ({existing.Id.Value}); " +
                "use the rotation flow instead of re-provisioning.");
        }

        var clientId = MintClientId(institutionCode);
        var clientSecret = GenerateClientSecret();
        var expiresAt = DateTimeOffset.UtcNow.AddYears(DefaultCredTtlYears);
        var credentialHash = _hasher.Hash(clientSecret);

        // The tenant row's initial QR capability choice (set by
        // POST /v1/admin/tenants) is inherited verbatim so the new
        // tenant_configurations row starts in lockstep with the tenant.
        // Post-issuance flips live on TenantConfiguration via the dedicated
        // Enable/Disable verbs — see DisableQrGeneration / EnableQrGeneration
        // / DisableQrValidation / EnableQrValidation.
        var configuration = TenantConfiguration.Register(
            tenantId: tenantId,
            clientId: clientId,
            clientSecretHash: credentialHash,
            expiresAt: expiresAt,
            isQrGenerationAllowed: isQrGenerationAllowed,
            isQrValidationAllowed: isQrValidationAllowed);

        await _configurations.AddAsync(configuration, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.LogAsync(
            new AuditEntry(
                Action: "auth.client_credentials.issued",
                ActorId: "system",
                ResourceType: "TenantConfiguration",
                ResourceId: configuration.Id.Value.ToString(),
                Metadata: $"{{\"tenant_id\":\"{tenantId:D}\",\"client_id\":\"{clientId}\"}}",
                TenantId: tenantId),
            cancellationToken).ConfigureAwait(false);

        return new ProvisionedConfiguration(
            CredentialId: configuration.Id.Value,
            ClientId: clientId,
            ClientSecret: clientSecret,
            ExpiresAt: expiresAt);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigurationSnapshot>> SuspendAllForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var configurations = await _configurations
            .GetAllByTenantAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        var suspended = new List<ConfigurationSnapshot>(configurations.Count);
        foreach (var configuration in configurations)
        {
            if (configuration.Status == TenantConfigurationStatus.Active)
            {
                configuration.Suspend();
                suspended.Add(new ConfigurationSnapshot(
                    CredentialId: configuration.Id.Value,
                    ClientId: configuration.ClientId));
            }
        }

        if (suspended.Count > 0)
        {
            await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var snap in suspended)
            {
                await _audit.LogAsync(
                    new AuditEntry(
                        Action: "tenant_configuration.suspended",
                        ActorId: "system",
                        ResourceType: "TenantConfiguration",
                        ResourceId: snap.CredentialId.ToString(),
                        Metadata: $"{{\"tenant_id\":\"{tenantId:D}\",\"client_id\":\"{snap.ClientId}\"}}",
                        TenantId: tenantId),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return suspended;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigurationSnapshot>> ReinstateAllForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var configurations = await _configurations
            .GetAllByTenantAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        var reinstated = new List<ConfigurationSnapshot>(configurations.Count);
        foreach (var configuration in configurations)
        {
            if (configuration.Status == TenantConfigurationStatus.Suspended)
            {
                configuration.Reinstate();
                reinstated.Add(new ConfigurationSnapshot(
                    CredentialId: configuration.Id.Value,
                    ClientId: configuration.ClientId));
            }
        }

        if (reinstated.Count > 0)
        {
            await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var snap in reinstated)
            {
                await _audit.LogAsync(
                    new AuditEntry(
                        Action: "tenant_configuration.reactivated",
                        ActorId: "system",
                        ResourceType: "TenantConfiguration",
                        ResourceId: snap.CredentialId.ToString(),
                        Metadata: $"{{\"tenant_id\":\"{tenantId:D}\",\"client_id\":\"{snap.ClientId}\"}}",
                        TenantId: tenantId),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return reinstated;
    }

    /// <inheritdoc/>
    public async Task<bool> HasActiveAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // Pure read; reuses the existing partial-UQ lookup. No audit row —
        // a gate probe is not a domain event.
        var existing = await _configurations
            .GetActiveByTenantAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        return existing is not null;
    }

    private static string MintClientId(string institutionCode)
    {
        // {institution_code-lowercase}-{8-hex}. Matches the sample shape in
        // docs/design/database-design.md §1.3 (e.g. 010101-7c1b4d88).
        var slug = Convert.ToHexString(RandomNumberGenerator.GetBytes(ClientIdHexBytes)).ToLowerInvariant();
        return $"{institutionCode.ToLowerInvariant()}-{slug}";
    }

    private static string GenerateClientSecret()
    {
        // 32 random bytes -> base64url (43 chars, no padding). Returned to the
        // caller once and never persisted.
        var bytes = RandomNumberGenerator.GetBytes(SecretBytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
