using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Queries.ListTenantApplications;

/// <summary>
/// Read-side handler for <see cref="ListTenantApplicationsQuery"/>. Pure
/// projection — no state change, no audit row.
/// </summary>
public sealed class ListTenantApplicationsQueryHandler
    : IRequestHandler<ListTenantApplicationsQuery, Result<IReadOnlyList<TenantApplicationListItem>>>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantApplicationRepository _applications;

    public ListTenantApplicationsQueryHandler(
        ITenantRepository tenants,
        ITenantApplicationRepository applications)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
    }

    public async Task<Result<IReadOnlyList<TenantApplicationListItem>>> Handle(
        ListTenantApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = await _tenants
            .GetByIdAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<IReadOnlyList<TenantApplicationListItem>>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId.Value:D} does not exist.");
        }

        var rows = await _applications
            .ListByTenantAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        var projected = rows.Select(Project).ToArray();

        return Result<IReadOnlyList<TenantApplicationListItem>>.Ok(projected);
    }

    private static TenantApplicationListItem Project(TenantApplication application) => new(
        TenantApplicationId: application.Id.Value,
        TenantId: application.TenantId.Value,
        Platform: application.Platform switch
        {
            TenantApplicationPlatform.Android => "ANDROID",
            TenantApplicationPlatform.Ios => "IOS",
            _ => application.Platform.ToString().ToUpperInvariant(),
        },
        PackageId: application.PackageId,
        Status: application.Status.ToString().ToUpperInvariant(),
        IsActive: application.IsActive,
        CreatedAt: application.CreatedAt,
        ModifiedAt: application.ModifiedAt);
}
