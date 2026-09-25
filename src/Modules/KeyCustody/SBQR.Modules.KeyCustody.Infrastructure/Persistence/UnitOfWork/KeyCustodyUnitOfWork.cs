using SBQR.Modules.KeyCustody.Domain.Interfaces;

namespace SBQR.Modules.KeyCustody.Infrastructure.Persistence.UnitOfWork;

/// <summary>
/// EF Core implementation of <see cref="IKeyCustodyUnitOfWork"/> for the
/// KeyCustody bounded context. Delegates <see cref="SaveChangesAsync"/> to
/// the underlying <see cref="KeyCustodyDbContext"/>.
/// </summary>
public sealed class KeyCustodyUnitOfWork : IKeyCustodyUnitOfWork
{
    private readonly KeyCustodyDbContext _db;

    public KeyCustodyUnitOfWork(KeyCustodyDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
