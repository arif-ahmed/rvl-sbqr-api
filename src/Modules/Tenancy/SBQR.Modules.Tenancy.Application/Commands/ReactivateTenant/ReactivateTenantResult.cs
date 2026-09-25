using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Commands.ReactivateTenant;

/// <summary>
/// Result of <see cref="ReactivateTenantCommand"/>. Carries the post-reactivation
/// snapshot the controller projects into a <see cref="Contracts.TenantResponse"/>.
/// </summary>
/// <param name="TenantId">The tenant that was reactivated.</param>
/// <param name="Status">New status, always <see cref="TenantStatus.Active"/>.</param>
/// <param name="ReactivatedAt">UTC timestamp at which the transition was applied.</param>
public sealed record ReactivateTenantResult(
    TenantId TenantId,
    TenantStatus Status,
    DateTimeOffset ReactivatedAt);
