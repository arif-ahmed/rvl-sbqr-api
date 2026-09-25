using Microsoft.EntityFrameworkCore;
using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.Modules.KeyCustody.Domain.Interfaces;

namespace SBQR.Modules.KeyCustody.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICryptoKeyRepository"/>. Reads and
/// writes delegate to <see cref="DbSet{T}"/> LINQ queries on
/// <see cref="KeyCustodyDbContext"/>.
/// </summary>
public sealed class CryptoKeyRepository : ICryptoKeyRepository
{
    private readonly KeyCustodyDbContext _db;

    public CryptoKeyRepository(KeyCustodyDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public Task<CryptoKey?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var typedId = id is CryptoKeyId keyId
            ? keyId
            : new CryptoKeyId((Guid)id);

        return _db.CryptoKeys
            .FirstOrDefaultAsync(k => k.Id == typedId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddAsync(CryptoKey entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _db.CryptoKeys.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task RemoveAsync(CryptoKey entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _db.CryptoKeys.Remove(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(CryptoKey entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _db.CryptoKeys.Update(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<CryptoKey?> GetActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _db.CryptoKeys
            .FirstOrDefaultAsync(
                k => k.TenantId == tenantId && k.Status == CryptoKeyStatus.Active,
                cancellationToken);
    }

    /// <inheritdoc/>
    public Task<CryptoKey?> GetSuspendedByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _db.CryptoKeys
            .FirstOrDefaultAsync(
                k => k.TenantId == tenantId && k.Status == CryptoKeyStatus.Suspended,
                cancellationToken);
    }

    /// <inheritdoc/>
    public Task<CryptoKey?> GetLatestByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _db.CryptoKeys
            .Where(k => k.TenantId == tenantId)
            .OrderByDescending(k => k.KeyVersion)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<CryptoKey> Items, int TotalCount)> ListAsync(
        Guid? tenantId,
        CryptoKeyStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CryptoKeys.AsQueryable();

        if (tenantId is { } t)
        {
            query = query.Where(k => k.TenantId == t);
        }

        if (status is { } s)
        {
            query = query.Where(k => k.Status == s);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(k => k.TenantId)
            .ThenByDescending(k => k.KeyVersion)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }
}
