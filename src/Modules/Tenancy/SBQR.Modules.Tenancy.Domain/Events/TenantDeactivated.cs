using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.Tenancy.Domain.Events;

/// <summary>
/// Raised by <see cref="Aggregates.Tenant.Deactivate"/> when a tenant is moved into
/// the terminal <see cref="Aggregates.TenantStatus.Terminated"/> state and
/// <c>is_active</c> is set to <c>false</c>.
/// </summary>
public sealed record TenantDeactivated(
    TenantId TenantId,
    string Actor,
    string? Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;
