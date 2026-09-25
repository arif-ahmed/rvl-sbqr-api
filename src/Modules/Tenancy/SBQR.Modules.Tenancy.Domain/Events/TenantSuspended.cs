using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.Tenancy.Domain.Events;

/// <summary>
/// Raised by <see cref="Aggregates.Tenant.Suspend"/> when a tenant is moved into
/// <see cref="Aggregates.TenantStatus.Suspended"/>. The reason is optional free
/// text (audit-relevant but not domain-meaningful).
/// </summary>
public sealed record TenantSuspended(
    TenantId TenantId,
    string Actor,
    string? Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;
