using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.IdentityAccess.Infrastructure.Persistence.Interceptors;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that stamps the standard audit
/// columns (<c>created_by</c>, <c>created_at</c>, <c>modified_by</c>,
/// <c>modified_at</c>) on every entity implementing the shared-kernel
/// <see cref="IAuditableEntity"/> (currently <c>ApiCredential</c>). The actor
/// identity comes from <see cref="IActorProvider"/>.
///
/// This is a clone of Tenancy's interceptor — the two modules own independent
/// DbContexts so each carries its own. A future epic may hoist a single
/// cross-cutting implementation into the Audit module.
/// </summary>
public sealed class IdentityAuditColumnInterceptor : SaveChangesInterceptor, IAuditColumnInterceptor
{
    private readonly IActorProvider _actorProvider;

    public IdentityAuditColumnInterceptor(IActorProvider actorProvider)
    {
        _actorProvider = actorProvider ?? throw new ArgumentNullException(nameof(actorProvider));
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
    public void StampAuditColumns()
    {
        // Intentionally empty — see Tenancy interceptor remarks. The real
        // stamping runs from SavingChangesAsync via the DbContext overload.
    }

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
            return "system";
        }
    }
}
