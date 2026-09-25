using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Commands.ActivateTenant;

/// <summary>
/// Application-side result of <see cref="ActivateTenantCommand"/>. Carries the
/// tenant id, the post-transition <see cref="TenantStatus"/>, and the timestamp
/// at which the activation was applied.
/// </summary>
/// <param name="TenantId">The activated tenant id.</param>
/// <param name="Status">Always <see cref="TenantStatus.Active"/> on success.</param>
/// <param name="ActivatedAt">Server timestamp of the transition.</param>
public sealed record ActivateTenantResult(
    TenantId TenantId,
    TenantStatus Status,
    DateTimeOffset ActivatedAt);