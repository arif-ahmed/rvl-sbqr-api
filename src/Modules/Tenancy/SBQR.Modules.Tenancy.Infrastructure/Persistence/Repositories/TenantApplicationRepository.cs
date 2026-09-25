using Microsoft.EntityFrameworkCore;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITenantApplicationRepository"/>. Every
/// write goes through this class; reads delegate to <see cref="DbSet{T}"/>
/// LINQ queries on <see cref="TenancyDbContext"/>.
/// </summary>
public sealed class TenantApplicationRepository : ITenantApplicationRepository
{
    private readonly TenancyDbContext _db;

    public TenantApplicationRepository(TenancyDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public Task<TenantApplication?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var typedId = id is TenantApplicationId applicationId
            ? applicationId
            : new TenantApplicationId((Guid)id);

        return _db.TenantApplications
            .FirstOrDefaultAsync(a => a.Id == typedId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddAsync(TenantApplication entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _db.TenantApplications.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task RemoveAsync(TenantApplication entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _db.TenantApplications.Remove(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(TenantApplication entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _db.TenantApplications.Update(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> ExistsActiveForTenantAsync(
        TenantId tenantId,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return _db.TenantApplications
            .AsNoTracking()
            .AnyAsync(
                a => a.TenantId == tenantId
                    && a.PackageId == packageId
                    && a.Status == TenantApplicationStatus.Active
                    && a.IsActive,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TenantApplication>> ListByTenantAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _db.TenantApplications
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.Platform)
            .ThenBy(a => a.PackageId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
