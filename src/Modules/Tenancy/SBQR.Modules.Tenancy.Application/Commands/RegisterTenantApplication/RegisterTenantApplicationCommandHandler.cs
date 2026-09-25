using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Application.Commands.RegisterTenantApplication;

/// <summary>
/// Handler for <see cref="RegisterTenantApplicationCommand"/>. Validated input
/// is guaranteed (the <c>ValidationBehavior&lt;,&gt;</c> runs first), so this
/// method only worries about:
/// <list type="number">
///   <item>Loading the owning tenant (404 when absent).</item>
///   <item>Building a <see cref="TenantApplication"/> via
///         <see cref="TenantApplication.Register"/>.</item>
///   <item>Persisting the row and translating the
///         <c>ix_tenant_applications_platform_package_id</c> UNIQUE violation
///         into <see cref="ErrorCode.InvariantViolation"/> so the controller
///         returns 409.</item>
/// </list>
/// </summary>
public sealed class RegisterTenantApplicationCommandHandler
    : IRequestHandler<RegisterTenantApplicationCommand, Result<RegisterTenantApplicationResult>>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantApplicationRepository _applications;
    private readonly IUnitOfWork _uow;
    private readonly IActorProvider _actor;

    public RegisterTenantApplicationCommandHandler(
        ITenantRepository tenants,
        ITenantApplicationRepository applications,
        IUnitOfWork uow,
        IActorProvider actor)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
    }

    public async Task<Result<RegisterTenantApplicationResult>> Handle(
        RegisterTenantApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = await _tenants
            .GetByIdAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<RegisterTenantApplicationResult>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId.Value:D} does not exist.");
        }

        var actorId = _actor.CurrentActor();
        var application = TenantApplication.Register(
            tenantId: request.TenantId,
            platform: request.Platform,
            packageId: request.PackageId,
            actor: actorId);

        await _applications.AddAsync(application, cancellationToken).ConfigureAwait(false);

        try
        {
            await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return Result<RegisterTenantApplicationResult>.Failure(
                ErrorCode.InvariantViolation,
                $"application (platform='{request.Platform}', package_id='{request.PackageId}') " +
                "is already registered.");
        }

        return Result<RegisterTenantApplicationResult>.Ok(new RegisterTenantApplicationResult(
            TenantApplicationId: application.Id,
            TenantId: application.TenantId,
            Platform: application.Platform,
            PackageId: application.PackageId,
            RegisteredAt: application.CreatedAt == default
                ? DateTimeOffset.UtcNow
                : application.CreatedAt));
    }

    /// <summary>
    /// True iff the underlying exception is PostgreSQL error 23505 (unique
    /// violation). Npgsql throws <c>Npgsql.PostgresException</c> with
    /// <c>SqlState = "23505"</c> which EF Core wraps in
    /// <c>DbUpdateException</c>. We compare the inner exception's full type
    /// name and SqlState via reflection to keep the Tenancy.Application
    /// assembly free of direct Npgsql / EF Core references
    /// (Tenancy.Application.csproj intentionally depends only on
    /// Contracts + Domain + SharedKernel — see the project file header).
    /// </summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var type = current.GetType();
            if (type.FullName == "Npgsql.PostgresException")
            {
                var sqlState = type.GetProperty("SqlState")?.GetValue(current) as string;
                if (string.Equals(sqlState, "23505", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
