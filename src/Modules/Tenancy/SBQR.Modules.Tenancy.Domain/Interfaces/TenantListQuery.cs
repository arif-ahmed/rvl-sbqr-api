using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Domain.Interfaces;

/// <summary>
/// Domain-level filter shape for <see cref="ITenantRepository.ListAsync"/>.
/// Lives in Domain (not Application) because the filter parameters describe
/// properties of the <see cref="Tenant"/> aggregate itself, not the API or
/// application use-case. Status and IsActive are optional — null means
/// "no filter on this field".
///
/// <para>Pagination is 1-based (page 1 is the first page). The validator on
/// the Application-side query enforces the bounds; the repository assumes
/// already-validated input.</para>
/// </summary>
/// <param name="Status">Optional status filter.</param>
/// <param name="IsActive">Optional soft-delete flag filter.</param>
/// <param name="Page">1-based page index.</param>
/// <param name="PageSize">Items per page.</param>
public sealed record TenantListQuery(
    TenantStatus? Status,
    bool? IsActive,
    int Page,
    int PageSize);