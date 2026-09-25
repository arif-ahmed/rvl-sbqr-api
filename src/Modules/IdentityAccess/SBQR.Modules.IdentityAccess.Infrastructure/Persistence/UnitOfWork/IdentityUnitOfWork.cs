using SBQR.Modules.IdentityAccess.Application.Abstractions;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.IdentityAccess.Infrastructure.Persistence.UnitOfWork;

/// <summary>
/// EF Core implementation of <see cref="IIdentityUnitOfWork"/> for the
/// Identity &amp; Access bounded context. Delegates
/// <see cref="SaveChangesAsync"/> to <see cref="IdentityDbContext"/> — the
/// audit interceptor attached in <see cref="IdentityAccessModule.RegisterServices"/>
/// runs as part of that call and stamps audit columns on tracked
/// <see cref="Domain.Aggregates.ApiCredential"/> instances.
/// </summary>
public sealed class IdentityUnitOfWork : IIdentityUnitOfWork
{
    private readonly IdentityDbContext _db;

    /// <summary>
    /// Construct the unit of work with the Identity DbContext.
    /// </summary>
    /// <param name="db">The Identity DbContext registered as scoped in the composition root.</param>
    public IdentityUnitOfWork(IdentityDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
