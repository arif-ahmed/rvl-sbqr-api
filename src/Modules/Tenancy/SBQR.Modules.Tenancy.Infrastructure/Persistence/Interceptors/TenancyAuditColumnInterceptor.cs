using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence.Interceptors;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that stamps the standard audit
/// columns (<c>created_by</c>, <c>created_at</c>, <c>modified_by</c>,
/// <c>modified_at</c>) on every entity implementing
/// <see cref="IAuditableEntity"/>. The interface is intentionally narrow:
/// <list type="bullet">
///   <item><c>created_by</c> / <c>created_at</c> — set once, on <c>EntityState.Added</c>.</item>
///   <item><c>modified_by</c> / <c>modified_at</c> — set on every <c>Added</c> AND
///         <c>Modified</c> entry, with <see cref="DateTimeOffset.UtcNow"/>.</item>
/// </list>
/// The actor identity comes from <see cref="IActorProvider"/>; the (nullable)
/// tenant id comes from <see cref="ICurrentTenant"/>. The
/// <see cref="StampAuditColumns"/> method (the only member of
/// <see cref="IAuditColumnInterceptor"/>) is invoked from this interceptor so
/// the Shared Kernel seam remains satisfied.
/// </summary>
public sealed class TenancyAuditColumnInterceptor : SaveChangesInterceptor, IAuditColumnInterceptor
{
    private readonly IActorProvider _actorProvider;
    private readonly ICurrentTenant _currentTenant;

    public TenancyAuditColumnInterceptor(
        IActorProvider actorProvider,
        ICurrentTenant currentTenant)
    {
        _actorProvider = actorProvider ?? throw new ArgumentNullException(nameof(actorProvider));
        _currentTenant = currentTenant ?? throw new ArgumentNullException(nameof(currentTenant));
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        StampAuditColumns(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        StampAuditColumns(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The shared kernel seam is a no-op here — the actual stamping runs from
    /// <see cref="SavingChangesAsync"/>/<see cref="SavingChanges"/> via the
    /// richer <see cref="StampAuditColumns(DbContext?)"/> overload. The interface
    /// exists so cross-module code (tests, future cross-cutting audit
    /// abstractions) can ask "is this interceptor wired?" without coupling to EF
    /// Core types.
    /// </remarks>
    public void StampAuditColumns()
    {
        // Intentionally empty — see remarks.
    }

    /// <summary>
    /// Walks every tracked entry on <paramref name="context"/>; for each one
    /// implementing <see cref="IAuditableEntity"/>, sets the appropriate audit
    /// properties based on <see cref="EntityEntry.State"/>.
    /// </summary>
    /// <param name="context">The DbContext whose change tracker is being stamped.</param>
    public void StampAuditColumns(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var actor = SafeActor();
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not IAuditableEntity auditable)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    auditable.CreatedBy = actor;
                    auditable.CreatedAt = now;
                    auditable.ModifiedBy = actor;
                    auditable.ModifiedAt = now;
                    break;

                case EntityState.Modified:
                    // Guard against EF rewriting Created_* on subsequent saves.
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;

                    auditable.ModifiedBy = actor;
                    auditable.ModifiedAt = now;
                    break;
            }
        }
    }

    private string SafeActor()
    {
        try
        {
            var actor = _actorProvider.CurrentActor();
            return string.IsNullOrWhiteSpace(actor) ? "system" : actor;
        }
        catch
        {
            // Audit must never fail a write because of a transient resolver issue.
            return "system";
        }
    }
}
