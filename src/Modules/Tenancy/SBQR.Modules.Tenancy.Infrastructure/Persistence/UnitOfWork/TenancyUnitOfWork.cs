using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence.UnitOfWork;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/> for the Tenancy &amp; Access
/// bounded context. Delegates <see cref="SaveChangesAsync"/> to the underlying
/// <see cref="TenancyDbContext"/> — the audit interceptor attached in
/// <see cref="TenancyModule.RegisterServices"/> runs as part of that call and
/// stamps <c>created_by/at</c> and <c>modified_by/at</c> on tracked
/// <see cref="Tenant"/> instances before the SQL is dispatched.
/// </summary>
public sealed class TenancyUnitOfWork : IUnitOfWork
{
    private readonly TenancyDbContext _db;

    /// <summary>
    /// Construct the unit of work with the Tenancy DbContext.
    /// </summary>
    /// <param name="db">The Tenancy DbContext registered as scoped in the composition root.</param>
    public TenancyUnitOfWork(TenancyDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns the number of state entries written to the database (mirrors
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>).
    /// </remarks>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
