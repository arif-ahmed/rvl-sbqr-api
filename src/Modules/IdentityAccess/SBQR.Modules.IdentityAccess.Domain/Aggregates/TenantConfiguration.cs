using System.Text.RegularExpressions;
using SBQR.Modules.IdentityAccess.Domain.Events;
using SBQR.SharedKernel.Domain;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.IdentityAccess.Domain.Aggregates;

/// <summary>
/// Aggregate root for the per-tenant configuration row that holds the OAuth 2.1
/// client-credential pair (client_id + Argon2id-hashed client_secret) AND two
/// capability flags the QR QrGeneration / Verification flows gate on
/// (<see cref="IsQrGenerationAllowed"/>, <see cref="IsQrValidationAllowed"/>).
///
/// <para>
/// One <see cref="TenantConfiguration"/> per tenant at issuance time. The
/// aggregate was renamed from <c>ApiCredential</c> together with the underlying
/// table (<c>identity.api_credentials</c> → <c>public.tenant_configurations</c>)
/// to acknowledge that the row now carries configuration flags in addition to
/// credentials. The OAuth 2.1 client-credentials surface still lives here —
/// the credential IS the machine identity.
/// </para>
///
/// <para>The owning tenant is referenced by an opaque <see cref="Guid"/>:
/// the strongly-typed <c>TenantId</c> value object belongs to
/// Tenancy.Domain and must not cross the boundary. The id is set at issuance
/// time (from the already-persisted tenant) and immutable.</para>
///
/// Invariants:
/// <list type="number">
///   <item><c>client_id</c> is unique and shaped <c>{institution_code-lowercase}-{8-hex}</c>
///         (e.g. <c>mtb-7c1b4d88</c>) — matches the sample rows in
///         <c>docs/design/database-design.md</c> §1.3.</item>
///   <item><c>client_secret_hash</c> is an Argon2id PHC-format string starting
///         with <c>$argon2id$</c>. The cleartext secret is never stored — only the
///         hash. Plaintext is returned to the caller <b>once</b> via the response
///         DTO, never via the domain.</item>
///   <item><c>is_qr_generation_allowed</c> and <c>is_qr_validation_allowed</c>
///         start at <c>true</c> by default and are flipped by an admin when a
///         tenant's QR privilege needs to be revoked without invalidating the
///         credential (suspend-credential vs. revoke-privilege are distinct
///         actions; <see cref="Suspend"/> handles the former,
///         <see cref="DisableQrGeneration"/>/<see cref="DisableQrValidation"/>
///         handle the latter).</item>
///   <item><c>status</c> starts at <see cref="TenantConfigurationStatus.Active"/>;
///         transitions to <see cref="TenantConfigurationStatus.Revoked"/> /
///         <see cref="TenantConfigurationStatus.Expired"/> /
///         <see cref="TenantConfigurationStatus.PendingRotation"/> land in the rotation story.</item>
///   <item><c>tenant_id</c> is set at issuance time and immutable.</item>
/// </list>
/// </summary>
public sealed class TenantConfiguration : AggregateRoot<TenantConfigurationId>, IAuditableEntity
{
    /// <summary>
    /// PHC-format guard: a valid Argon2id encoded hash starts with <c>$argon2id$</c>.
    /// Kept as a single source of truth so the constructor and any future verifiers
    /// can't disagree.
    /// </summary>
    private static readonly Regex Argon2idFormat = new(
        pattern: @"^\$argon2id\$v=19\$m=\d+,t=\d+,p=\d+\$[A-Za-z0-9+/]+\$[A-Za-z0-9+/]+$",
        options: RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Constructor is private: only the static factory can build a TenantConfiguration.
    private TenantConfiguration(
        TenantConfigurationId id,
        Guid tenantId,
        string clientId,
        string clientSecretHash,
        TenantConfigurationStatus status,
        DateTimeOffset? expiresAt,
        bool isQrGenerationAllowed,
        bool isQrValidationAllowed)
        : base(id)
    {
        TenantId = tenantId;
        ClientId = clientId;
        ClientSecretHash = clientSecretHash;
        Status = status;
        ExpiresAt = expiresAt;
        IsQrGenerationAllowed = isQrGenerationAllowed;
        IsQrValidationAllowed = isQrValidationAllowed;
        IsActive = true;
    }

    /// <summary>The tenant that owns this configuration row (opaque id; Tenancy owns the aggregate). Set at issuance, immutable.</summary>
    public Guid TenantId { get; }

    /// <summary>
    /// The opaque client identifier presented to the token endpoint.
    /// Format: <c>{institution_code-lowercase}-{8-hex}</c>.
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// The Argon2id PHC-format hash of the client secret. Plaintext secret is
    /// never persisted.
    /// </summary>
    public string ClientSecretHash { get; }

    /// <summary>Lifecycle state — see <see cref="TenantConfigurationStatus"/>.</summary>
    public TenantConfigurationStatus Status { get; private set; }

    /// <summary>UTC timestamp after which the configuration must be rejected. Null = open-ended.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>
    /// UTC timestamp of the last successful authentication using this credential.
    /// Stamped by the token-endpoint handler on every successful mint so
    /// credential-hygiene tooling can detect dormant clients.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>
    /// Stamp the credential's last successful authentication time. The only
    /// legitimate caller is the token-endpoint handler on a successful mint;
    /// private setter keeps the field solely aggregate-owned.
    /// </summary>
    public void RecordUsage() => LastUsedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gate for the QR QrGeneration flow. <c>false</c> means the tenant may NOT
    /// mint new QR payloads even if their credential is otherwise <see cref="TenantConfigurationStatus.Active"/>.
    /// Mutated by <see cref="DisableQrGeneration"/> / <see cref="EnableQrGeneration"/>.
    /// </summary>
    public bool IsQrGenerationAllowed { get; private set; }

    /// <summary>
    /// Gate for the QR Verification flow. <c>false</c> means the tenant may NOT
    /// validate QR payloads against their own signing keys.
    /// Mutated by <see cref="DisableQrValidation"/> / <see cref="EnableQrValidation"/>.
    /// </summary>
    public bool IsQrValidationAllowed { get; private set; }

    /// <summary>
    /// Soft-delete flag. <c>false</c> means the configuration row is preserved
    /// for history but excluded from active queries (partial UQ on
    /// <c>tenant_configurations</c> allows multiple non-active rows per tenant).
    /// </summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc/>
    public string? CreatedBy { get; set; }

    /// <inheritdoc/>
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc/>
    public string? ModifiedBy { get; set; }

    /// <inheritdoc/>
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>
    /// Register a brand-new configuration row for <paramref name="tenantId"/>. The
    /// <paramref name="clientSecretHash"/> must already be computed by the
    /// caller (<see cref="SBQR.Modules.IdentityAccess.Application.Abstractions.ISecretHasher"/>);
    /// this factory NEVER hashes plaintext. Capability flags default to <c>true</c>.
    /// </summary>
    /// <param name="tenantId">Owning tenant id (opaque Guid across the module boundary).</param>
    /// <param name="clientId">Minted <c>client_id</c> string; max 100 chars (validator enforces).</param>
    /// <param name="clientSecretHash">Argon2id PHC-format hash. Plaintext is not accepted.</param>
    /// <param name="expiresAt">Expiry timestamp; <c>null</c> for open-ended.</param>
    /// <param name="isQrGenerationAllowed">Capability flag for the QR QrGeneration flow. Defaults to <c>true</c>.</param>
    /// <param name="isQrValidationAllowed">Capability flag for the QR Verification flow. Defaults to <c>true</c>.</param>
    /// <returns>The freshly created <see cref="TenantConfiguration"/>.</returns>
    public static TenantConfiguration Register(
        Guid tenantId,
        string clientId,
        string clientSecretHash,
        DateTimeOffset? expiresAt,
        bool isQrGenerationAllowed = true,
        bool isQrValidationAllowed = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecretHash);

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("tenantId must not be an empty Guid.", nameof(tenantId));
        }

        if (!Argon2idFormat.IsMatch(clientSecretHash))
        {
            throw new ArgumentException(
                "client_secret_hash must be a PHC-format Argon2id encoded string ($argon2id$v=19$…).",
                nameof(clientSecretHash));
        }

        var configuration = new TenantConfiguration(
            id: new TenantConfigurationId(Guid.NewGuid()),
            tenantId: tenantId,
            clientId: clientId,
            clientSecretHash: clientSecretHash,
            status: TenantConfigurationStatus.Active,
            expiresAt: expiresAt,
            isQrGenerationAllowed: isQrGenerationAllowed,
            isQrValidationAllowed: isQrValidationAllowed);

        configuration.RaiseDomainEvent(new TenantConfigurationCreated(
            TenantId: tenantId,
            TenantConfigurationId: configuration.Id,
            ClientId: clientId,
            ExpiresAt: expiresAt,
            IsQrGenerationAllowed: isQrGenerationAllowed,
            IsQrValidationAllowed: isQrValidationAllowed,
            OccurredAt: DateTimeOffset.UtcNow));

        return configuration;
    }

    /// <summary>
    /// Cascade-suspend the credential. Invoked (through the IdentityAccess
    /// Contracts seam) when the owning tenant is moved into a suspended state
    /// by the Tenancy module. Throws on self-transition; the DB CHECK
    /// constraint also rejects any other state. Capability flags are
    /// untouched — suspend affects authn, not authz.
    /// </summary>
    public void Suspend()
    {
        if (Status == TenantConfigurationStatus.Suspended)
        {
            throw new InvalidOperationException(
                $"TenantConfiguration {Id} is already Suspended; Suspend is a no-op and must not raise a duplicate event.");
        }

        if (Status != TenantConfigurationStatus.Active)
        {
            throw new InvalidOperationException(
                $"TenantConfiguration {Id} cannot be Suspended from '{Status}'; only ACTIVE configurations can be suspended.");
        }

        Status = TenantConfigurationStatus.Suspended;

        RaiseDomainEvent(new TenantConfigurationSuspended(
            TenantId: TenantId,
            TenantConfigurationId: Id,
            OccurredAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Cascade-reinstate the credential. Invoked (through the IdentityAccess
    /// Contracts seam) when the owning tenant leaves the suspended state.
    /// Only <see cref="TenantConfigurationStatus.Suspended"/> configurations
    /// can be reinstated; any other state (e.g. <see cref="TenantConfigurationStatus.Revoked"/>)
    /// is a coding error, not a recoverable transition.
    /// </summary>
    public void Reinstate()
    {
        if (Status == TenantConfigurationStatus.Active)
        {
            throw new InvalidOperationException(
                $"TenantConfiguration {Id} is already Active; Reinstate is a no-op and must not raise a duplicate event.");
        }

        if (Status != TenantConfigurationStatus.Suspended)
        {
            throw new InvalidOperationException(
                $"TenantConfiguration {Id} cannot be Reinstated from '{Status}'; only SUSPENDED configurations can be reinstated.");
        }

        Status = TenantConfigurationStatus.Active;

        RaiseDomainEvent(new TenantConfigurationReinstated(
            TenantId: TenantId,
            TenantConfigurationId: Id,
            OccurredAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Revoke the tenant's QR-generation privilege without invalidating the
    /// credential. The token endpoint still accepts the credential; only the
    /// QR QrGeneration flow rejects it.
    /// </summary>
    public void DisableQrGeneration()
    {
        if (!IsQrGenerationAllowed)
        {
            return; // already disabled; idempotent
        }

        IsQrGenerationAllowed = false;
    }

    /// <summary>Re-enable QR generation after a <see cref="DisableQrGeneration"/>.</summary>
    public void EnableQrGeneration()
    {
        if (IsQrGenerationAllowed)
        {
            return; // already enabled; idempotent
        }

        IsQrGenerationAllowed = true;
    }

    /// <summary>
    /// Revoke the tenant's QR-validation privilege without invalidating the
    /// credential. The token endpoint still accepts the credential; only the
    /// QR Verification flow rejects it.
    /// </summary>
    public void DisableQrValidation()
    {
        if (!IsQrValidationAllowed)
        {
            return; // already disabled; idempotent
        }

        IsQrValidationAllowed = false;
    }

    /// <summary>Re-enable QR validation after a <see cref="DisableQrValidation"/>.</summary>
    public void EnableQrValidation()
    {
        if (IsQrValidationAllowed)
        {
            return; // already enabled; idempotent
        }

        IsQrValidationAllowed = true;
    }
}
