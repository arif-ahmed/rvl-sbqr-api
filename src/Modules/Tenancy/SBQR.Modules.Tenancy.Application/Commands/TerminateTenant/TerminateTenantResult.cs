using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Commands.TerminateTenant;

/// <summary>
/// Result of <see cref="TerminateTenantCommand"/>. Carries the post-termination
/// snapshot the controller projects into a <see cref="Contracts.TenantResponse"/>.
/// </summary>
/// <param name="TenantId">The tenant that was terminated.</param>
/// <param name="Status">New status, always <see cref="TenantStatus.Terminated"/>.</param>
/// <param name="Reason">The reason supplied with the request, if any.</param>
/// <param name="TerminatedAt">UTC timestamp at which the transition was applied.</param>
public sealed record TerminateTenantResult(
    TenantId TenantId,
    TenantStatus Status,
    string? Reason,
    DateTimeOffset TerminatedAt);