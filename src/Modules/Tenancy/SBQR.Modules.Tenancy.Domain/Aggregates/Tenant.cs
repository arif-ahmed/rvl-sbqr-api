using SBQR.Modules.Tenancy.Domain.Events;
using SBQR.SharedKernel.Domain;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Domain.Aggregates;

/// <summary>
/// Aggregate root for the Tenancy &amp; Access bounded context. A <see cref="Tenant"/>
/// represents one of our customers (a bank, MFS, or PSP integrating with the SBQR
/// Platform). <see cref="Tenant"/> is the consistency boundary — every invariant the
/// platform guarantees about a tenant is enforced here, not in the DB or the API.
///
/// Lifecycle:
/// <list type="bullet">
///   <item><see cref="Register"/> creates a tenant in <see cref="TenantStatus.Pending"/>.</item>
///   <item><see cref="Activate"/>, <see cref="Suspend"/>, <see cref="Deactivate"/> move
///         it through the documented state machine.</item>
/// </list>
///
/// Invariants:
/// <list type="number">
///   <item><c>institution_code</c> is set at registration and immutable. It mirrors
///         <c>institution_registries.institution_code</c>; overlap is detected via JOIN,
///         never via FK.</item>
///   <item>Cannot leave <see cref="IsActive"/> = <c>true</c> while
///         <see cref="Status"/> is <see cref="TenantStatus.Active"/> or
///         <see cref="TenantStatus.Pending"/> — except via
///         <see cref="Deactivate"/>, which moves to
///         <see cref="TenantStatus.Terminated"/> and sets <see cref="IsActive"/> to
///         <c>false</c> atomically.</item>
///   <item>Self-transitions (<see cref="Activate"/> on an already-Active tenant,
///         etc.) are rejected with an <see cref="ArgumentException"/>.</item>
/// </list>
/// </summary>
public sealed class Tenant : AggregateRoot<TenantId>, IAuditableEntity
{
    // Constructor is private: only the static factory can build a Tenant.
    // EF Core rehydration uses the parameterless-or-internal seam in Infrastructure.
    private Tenant(
        TenantId id,
        string institutionName,
        string institutionCode,
        TenantStatus status,
        bool isActive)
        : base(id)
    {
        InstitutionName = institutionName;
        InstitutionCode = institutionCode;
        Status = status;
        IsActive = isActive;
    }

    /// <summary>
    /// Human-readable name of the tenant institution. May be edited post-registration
    /// (not modeled here — left for a future "update tenant profile" story).
    /// </summary>
    public string InstitutionName { get; private set; }

    /// <summary>
    /// Bangladesh Bank–assigned institution code (six digits, mirrors
    /// <c>institution_registries.institution_code</c>). Immutable for the
    /// aggregate's lifetime — set in <see cref="Register"/> and never
    /// reassigned. Required: a tenant only exists once BB has issued the code,
    /// so the column is NOT NULL at the DB layer.
    /// </summary>
    public string InstitutionCode { get; }

    /// <summary>Current lifecycle state.</summary>
    public TenantStatus Status { get; private set; }

    /// <summary>
    /// Soft-delete flag. <c>false</c> means the row is preserved for history but
    /// excluded from active queries (partial index <c>ix_*_active WHERE is_active = TRUE</c>).
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
    /// Factory that creates a brand-new <see cref="Tenant"/> in
    /// <see cref="TenantStatus.Pending"/> with <see cref="IsActive"/> = <c>true</c>.
    /// Raises <see cref="TenantRegistered"/> so subscribers can record the creation.
    /// </summary>
    /// <param name="institutionName">Human-readable name; required.</param>
    /// <param name="institutionCode">BB institution code, exactly six digits.
    ///     Format is enforced by
    ///     <c>SBQR.Modules.Tenancy.Application.Commands.CreateTenant.CreateTenantValidator</c>.</param>
    public static Tenant Register(
        string institutionName,
        string institutionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionCode);

        var tenant = new Tenant(
            id: new TenantId(Guid.NewGuid()),
            institutionName: institutionName,
            institutionCode: institutionCode,
            status: TenantStatus.Pending,
            isActive: true);

        tenant.RaiseDomainEvent(new TenantRegistered(
            TenantId: tenant.Id,
            InstitutionName: tenant.InstitutionName,
            InstitutionCode: tenant.InstitutionCode,
            OccurredAt: DateTimeOffset.UtcNow));

        return tenant;
    }

    /// <summary>
    /// Move a <see cref="TenantStatus.Pending"/> or <see cref="TenantStatus.Suspended"/>
    /// tenant into <see cref="TenantStatus.Active"/>. Re-raises on self-transition.
    /// </summary>
    public void Activate(string actor)
    {
        // Backwards-compatible overload used by unit tests that drive the
        // aggregate directly without exercising the activate-gate. The
        // production code path (Activate / Reactivate command handlers)
        // always calls the readiness-aware overload below — the production
        // gate is enforced there, not here.
        Activate(actor, readiness: TenantReadiness.AllReady);
    }

    /// <summary>
    /// Readiness-aware activation. The Tenancy <c>Activate</c> and
    /// <c>Reactivate</c> handlers read three flags (FI credential,
    /// signing key, trust-directory row) from the existing Contracts
    /// seams, build a <see cref="TenantReadiness"/>, and pass it here.
    /// The aggregate refuses the state transition if any flag is
    /// <c>false</c> — preventing a tenant from reaching
    /// <see cref="TenantStatus.Active"/> while missing one of the
    /// downstream resources it needs to actually transact.
    /// </summary>
    /// <param name="actor">Initiating actor (admin subject). Required.</param>
    /// <param name="readiness">Preconditions evaluated at the moment of activation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="readiness"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="TenantReadiness.IsReady"/> is <c>false</c>
    /// (the readiness summary is included in the message), when the
    /// tenant is already <see cref="TenantStatus.Active"/>, or when the
    /// tenant is <see cref="TenantStatus.Terminated"/>. The handler
    /// catches and maps to <c>409 InvariantViolation</c>.
    /// </exception>
    public void Activate(string actor, TenantReadiness readiness)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(readiness);

        if (!readiness.IsReady)
        {
            throw new InvalidOperationException(
                $"Tenant {Id} cannot be activated: {readiness}");
        }

        if (Status == TenantStatus.Active)
        {
            throw new InvalidOperationException(
                $"Tenant {Id} is already Active; Activate is a no-op and must not raise a duplicate event.");
        }

        if (Status == TenantStatus.Terminated)
        {
            throw new InvalidOperationException(
                $"Tenant {Id} is Terminated; Activate is not allowed from a terminal state.");
        }

        Status = TenantStatus.Active;
        IsActive = true;

        RaiseDomainEvent(new TenantActivated(
            TenantId: Id,
            Actor: actor,
            OccurredAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Move an <see cref="TenantStatus.Active"/> or <see cref="TenantStatus.Pending"/>
    /// tenant into <see cref="TenantStatus.Suspended"/>. Idempotent-rejected on
    /// self-transition.
    /// </summary>
    public void Suspend(string actor, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (Status == TenantStatus.Suspended)
        {
            throw new InvalidOperationException(
                $"Tenant {Id} is already Suspended; Suspend is a no-op and must not raise a duplicate event.");
        }

        if (Status == TenantStatus.Terminated)
        {
            throw new InvalidOperationException(
                $"Tenant {Id} is Terminated; Suspend is not allowed from a terminal state.");
        }

        Status = TenantStatus.Suspended;

        RaiseDomainEvent(new TenantSuspended(
            TenantId: Id,
            Actor: actor,
            Reason: reason,
            OccurredAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Move the tenant into the terminal <see cref="TenantStatus.Terminated"/>
    /// state and set <see cref="IsActive"/> to <c>false</c> atomically. Allowed
    /// from any non-terminal state. From <see cref="TenantStatus.Terminated"/> it
    /// throws — deactivation is a one-way trip.
    /// </summary>
    public void Deactivate(string actor, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (Status == TenantStatus.Terminated)
        {
            throw new InvalidOperationException(
                $"Tenant {Id} is already Terminated; Deactivate is not idempotent and must not raise a duplicate event.");
        }

        Status = TenantStatus.Terminated;
        IsActive = false;

        RaiseDomainEvent(new TenantDeactivated(
            TenantId: Id,
            Actor: actor,
            Reason: reason,
            OccurredAt: DateTimeOffset.UtcNow));
    }
}