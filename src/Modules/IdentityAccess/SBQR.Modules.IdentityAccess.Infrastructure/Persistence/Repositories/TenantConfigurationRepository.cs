using Microsoft.EntityFrameworkCore;
using SBQR.Modules.IdentityAccess.Domain.Aggregates;
using SBQR.Modules.IdentityAccess.Domain.Interfaces;

namespace SBQR.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITenantConfigurationRepository"/>. Reads
/// delegate to <see cref="DbSet{T}"/> LINQ queries on
/// <see cref="IdentityDbContext"/>.
/// </summary>
public sealed class TenantConfigurationRepository : ITenantConfigurationRepository
{
    private readonly IdentityDbContext _db;

    public TenantConfigurationRepository(IdentityDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public Task<TenantConfiguration?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var typedId = id is TenantConfigurationId configurationId
            ? configurationId
            : new TenantConfigurationId((Guid)id);

        return _db.TenantConfigurations
            .FirstOrDefaultAsync(c => c.Id == typedId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddAsync(TenantConfiguration entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _db.TenantConfigurations.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task RemoveAsync(TenantConfiguration entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _db.TenantConfigurations.Remove(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(TenantConfiguration entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _db.TenantConfigurations.Update(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<TenantConfiguration?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        return _db.TenantConfigurations
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TenantConfiguration?> GetActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _db.TenantConfigurations
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId
                     && c.Status == TenantConfigurationStatus.Active
                     && c.IsActive,
                cancellationToken);
    }

    /// <inheritdoc/>
    public Task<TenantConfiguration?> GetSuspendedByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _db.TenantConfigurations
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId
                     && c.Status == TenantConfigurationStatus.Suspended
                     && c.IsActive,
                cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TenantConfiguration>> GetAllByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.TenantConfigurations
            .Where(c => c.TenantId == tenantId && c.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows;
    }
}
