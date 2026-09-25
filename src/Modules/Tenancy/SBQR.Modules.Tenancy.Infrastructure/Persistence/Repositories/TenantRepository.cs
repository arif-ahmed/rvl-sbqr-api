using Microsoft.EntityFrameworkCore;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITenantRepository"/>. Every write goes
/// through this class; reads delegate to <see cref="DbSet{T}"/> LINQ queries on
/// <see cref="TenancyDbContext"/>.
/// </summary>
public sealed class TenantRepository : ITenantRepository
{
    private readonly TenancyDbContext _db;

    public TenantRepository(TenancyDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc/>
    public Task<Tenant?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var typedId = id is TenantId tenantId
            ? tenantId
            : new TenantId((Guid)id);

        return _db.Tenants
            .FirstOrDefaultAsync(t => t.Id == typedId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddAsync(Tenant entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _db.Tenants.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task RemoveAsync(Tenant entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _db.Tenants.Remove(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(Tenant entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _db.Tenants.Update(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<Tenant?> GetByInstitutionCodeAsync(string institutionCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionCode);
        return _db.Tenants.FirstOrDefaultAsync(t => t.InstitutionCode == institutionCode, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<Tenant> Items, int TotalCount)> ListAsync(
        TenantListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Base query — read-only path uses AsNoTracking so the change tracker
        // doesn't pin the projected entities.
        var filtered = _db.Tenants.AsNoTracking().AsQueryable();

        if (query.Status is { } status)
        {
            filtered = filtered.Where(t => t.Status == status);
        }

        if (query.IsActive is { } isActive)
        {
            filtered = filtered.Where(t => t.IsActive == isActive);
        }

        // Total count comes from the filtered query BEFORE paging — so the
        // admin console can show "1-20 of 347" even when page 1 only has 20.
        var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        // Stable ordering by created_at then tenant_id — keeps pagination
        // consistent across requests (no "shifting" results when a new tenant
        // is inserted between page requests).
        var items = await filtered
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }
}
