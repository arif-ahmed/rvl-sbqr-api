using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SBQR.Modules.QrGeneration.Domain.Aggregates;
using SBQR.SharedKernel.Application;

// The class name `QrGeneration` collides with the namespace `Aggregates` (where
// it lives). A type alias lets us reference the aggregate cleanly without
// fully qualifying every use.
using QrGenerationAggregate = SBQR.Modules.QrGeneration.Domain.Aggregates.QrGeneration;

namespace SBQR.Modules.QrGeneration.Infrastructure.Persistence.Interceptors;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that stamps
/// <c>created_by</c> and <c>created_at</c> on <see cref="QrGeneration"/>
/// aggregates as they are inserted. The actor identity comes from
/// <see cref="IActorProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the structure of <c>TenancyAuditColumnInterceptor</c>, but is
/// scoped to <see cref="QrGeneration"/> because <c>public.qr_generations</c>
/// carries only <c>created_*</c> columns — it does NOT persist
/// <c>modified_by</c> / <c>modified_at</c>. Implementing
/// <c>IAuditableEntity</c> here would force EF Core to attempt mapping the
/// missing <c>modified_*</c> columns at flush and throw.
/// </para>
/// <para>
/// The interceptor targets the <see cref="QrGeneration"/> CLR type directly
/// (not a marker interface) so any future entity added to
/// <c>QrGenerationDbContext</c> is unaffected.
/// </para>
/// </remarks>
public sealed class QrGenerationAuditColumnInterceptor : SaveChangesInterceptor
{
    private readonly IActorProvider _actorProvider;

    public QrGenerationAuditColumnInterceptor(IActorProvider actorProvider)
    {
        _actorProvider = actorProvider ?? throw new ArgumentNullException(nameof(actorProvider));
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var actor = SafeActor();
        var stamp = DateTimeOffset.UtcNow;

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (entry.Entity is not QrGenerationAggregate gen)
            {
                continue;
            }

            // Only stamp when the caller didn't pre-populate (cheap forward
            // compat for future callers that may assign explicitly).
            if (string.IsNullOrEmpty(gen.CreatedBy))
            {
                gen.CreatedBy = actor;
            }

            if (gen.CreatedAt is null)
            {
                gen.CreatedAt = stamp;
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
