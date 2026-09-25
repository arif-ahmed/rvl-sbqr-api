using SBQR.Modules.Tenancy.Domain.Events;
using SBQR.SharedKernel.Domain;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Domain.Aggregates;

/// <summary>
/// Aggregate root for a single registered mobile-app package identifier that
/// belongs to a tenant. A row in <c>public.tenant_applications</c> is one
/// app (Android <c>applicationId</c> or iOS bundle ID) registered to one
/// tenant; the tenant's OAuth token endpoint consults this list at mint time
/// so a wrong / unregistered / suspended app fails closed (FR-AUTH-002).
///
/// <para>
/// Lifecycle:
/// <list type="bullet">
///   <item><see cref="Register"/> creates the row in <see cref="TenantApplicationStatus.Active"/>
///         with <see cref="IsActive"/> = <c>true</c>.</item>
///   <item><see cref="Suspend"/> flips <see cref="Status"/> to
///         <see cref="TenantApplicationStatus.Suspended"/> — the per-app kill
///         switch.</item>
///   <item><see cref="Reinstate"/> flips back to <see cref="TenantApplicationStatus.Active"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// Invariants:
/// <list type="number">
///   <item><c>package_id</c> is set at registration and is immutable for the
///         aggregate's lifetime. Re-pointing a row to a different package
///         requires a new row.</item>
///   <item><c>platform</c> is set at registration and is immutable — a single
///         row cannot be retargeted from Android to iOS or vice versa.</item>
///   <item>Self-transitions (<see cref="Suspend"/> on an already-Suspended row,
///         etc.) are rejected with <see cref="InvalidOperationException"/> —
///         the handler maps that to <see cref="SBQR.SharedKernel.Application.ErrorCode.InvariantViolation"/>
///         so the controller returns 409.</item>
/// </list>
/// </para>
///
/// <para>
/// <c>package_id</c> is an identifier, not a secret. It is stored in clear
/// (the same way Android/iOS expose it on every install), compared exactly,
/// never hashed. See FR-AUTH-002 §4.2.
/// </para>
/// </summary>
public sealed class TenantApplication : AggregateRoot<TenantApplicationId>, IAuditableEntity
{
    // Constructor is private: only the static factory can build a
    // TenantApplication. EF Core rehydration uses the parameterless-or-internal
    // seam in Infrastructure.
    private TenantApplication(
        TenantApplicationId id,
        TenantId tenantId,
        TenantApplicationPlatform platform,
        string packageId,
        TenantApplicationStatus status,
        bool isActive)
        : base(id)
    {
        TenantId = tenantId;
        Platform = platform;
        PackageId = packageId;
        Status = status;
        IsActive = isActive;
    }

    /// <summary>The owning tenant's id. Immutable.</summary>
    public TenantId TenantId { get; }

    /// <summary>The mobile platform this row describes. Immutable.</summary>
    public TenantApplicationPlatform Platform { get; }

    /// <summary>
    /// The mobile app's package identifier on <see cref="Platform"/> —
    /// Android <c>applicationId</c> or iOS bundle ID. Immutable; reverse-DNS
    /// format enforced at the API boundary by
    /// <c>RegisterTenantApplicationValidator</c>.
    /// </summary>
    public string PackageId { get; }

    /// <summary>Per-app lifecycle status.</summary>
    public TenantApplicationStatus Status { get; private set; }

    /// <summary>
    /// Soft-delete flag. <c>false</c> means the row is preserved for history
    /// but excluded from active queries (partial index
    /// <c>ix_tenant_applications_active WHERE is_active = TRUE</c>) and from
    /// the auth hot-path allow-list.
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
    /// Factory that creates a brand-new <see cref="TenantApplication"/> in
    /// <see cref="TenantApplicationStatus.Active"/> with <see cref="IsActive"/>
    /// = <c>true</c>. Raises <see cref="TenantApplicationRegistered"/> so
    /// subscribers (Audit) can record the creation. The
    /// (platform, package_id) UNIQUE index in the database is the
    /// duplicate-application guard; the application-side validator pre-checks
    /// shape only (reverse-DNS, ≤200 chars), not uniqueness.
    ///
    /// <para>
    /// <b>Duplicate-application rule (FR-AUTH-002 / FR-AUTH-003 operational
    /// contract):</b> a tenant may register at most one row per
    /// <c>(platform, package_id)</c> pair. The same <c>package_id</c> is
    /// legal on a second platform — e.g. <c>com.dhakabank.consumer</c> as
    /// both an ANDROID row and an IOS row, one per store — but never twice
    /// on the same platform. The constraint is enforced by the
    /// <c>ix_tenant_applications_platform_package_id</c> UNIQUE index in
    /// migration <c>008_tenant_applications.sql</c>; the
    /// <see cref="RegisterTenantApplicationCommandHandler"/> catches PG
    /// <c>23505</c> and surfaces it as <c>409 InvariantViolation</c> via
    /// <see cref="SBQR.SharedKernel.Application.ErrorCode.InvariantViolation"/>.
    /// The application-side validator pre-checks shape only, because
    /// <c>IValidator</c> rules must be pure and cannot consult the DB.
    /// </para>
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="platform">Android or iOS.</param>
    /// <param name="packageId">The mobile package identifier. Required.</param>
    /// <param name="actor">Initiating actor (admin subject). Required.</param>
    public static TenantApplication Register(
        TenantId tenantId,
        TenantApplicationPlatform platform,
        string packageId,
        string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var application = new TenantApplication(
            id: new TenantApplicationId(Guid.NewGuid()),
            tenantId: tenantId,
            platform: platform,
            packageId: packageId,
            status: TenantApplicationStatus.Active,
            isActive: true);

        application.RaiseDomainEvent(new TenantApplicationRegistered(
            TenantApplicationId: application.Id,
            TenantId: application.TenantId,
            Platform: application.Platform,
            PackageId: application.PackageId,
            Actor: actor,
            OccurredAt: DateTimeOffset.UtcNow));

        return application;
    }

    /// <summary>
    /// Per-app kill switch: move an <see cref="TenantApplicationStatus.Active"/>
    /// row into <see cref="TenantApplicationStatus.Suspended"/>. Idempotent-rejected
    /// on self-transition. Does not affect sibling apps, the owning tenant, or
    /// the tenant's other credentials (FR-AUTH-002 §5.6 BR4).
    /// </summary>
    /// <param name="actor">Initiating actor (admin subject). Required.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the row is already <see cref="TenantApplicationStatus.Suspended"/>.
    /// The handler catches and maps to <c>409 InvariantViolation</c>.
    /// </exception>
    public void Suspend(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (Status == TenantApplicationStatus.Suspended)
        {
            throw new InvalidOperationException(
                $"TenantApplication {Id} is already Suspended; Suspend is a no-op and must not raise a duplicate event.");
        }

        Status = TenantApplicationStatus.Suspended;

        RaiseDomainEvent(new TenantApplicationSuspended(
            TenantApplicationId: Id,
            TenantId: TenantId,
            Platform: Platform,
            PackageId: PackageId,
            Actor: actor,
            OccurredAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Move a <see cref="TenantApplicationStatus.Suspended"/> row back to
    /// <see cref="TenantApplicationStatus.Active"/>. Idempotent-rejected on
    /// self-transition.
    /// </summary>
    /// <param name="actor">Initiating actor (admin subject). Required.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the row is already <see cref="TenantApplicationStatus.Active"/>.
    /// </exception>
    public void Reinstate(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (Status == TenantApplicationStatus.Active)
        {
            throw new InvalidOperationException(
                $"TenantApplication {Id} is already Active; Reinstate is a no-op and must not raise a duplicate event.");
        }

        Status = TenantApplicationStatus.Active;

        RaiseDomainEvent(new TenantApplicationReinstated(
            TenantApplicationId: Id,
            TenantId: TenantId,
            Platform: Platform,
            PackageId: PackageId,
            Actor: actor,
            OccurredAt: DateTimeOffset.UtcNow));
    }
}
