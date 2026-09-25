using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Commands.SuspendTenant;

/// <summary>
/// Result of <see cref="SuspendTenantCommand"/>. Carries the post-suspension
/// snapshot the controller projects into a <see cref="Contracts.TenantResponse"/>.
/// </summary>
/// <param name="TenantId">The tenant that was suspended.</param>
/// <param name="Status">New status, always <see cref="TenantStatus.Suspended"/>.</param>
/// <param name="Reason">The reason supplied with the request, if any.</param>
/// <param name="SuspendedAt">UTC timestamp at which the transition was applied.</param>
public sealed record SuspendTenantResult(
    TenantId TenantId,
    TenantStatus Status,
    string? Reason,
    DateTimeOffset SuspendedAt);
