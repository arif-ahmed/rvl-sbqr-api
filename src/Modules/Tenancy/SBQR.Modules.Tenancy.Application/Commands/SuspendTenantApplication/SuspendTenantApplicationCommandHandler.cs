using MediatR;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Application.Commands.SuspendTenantApplication;

/// <summary>
/// Handler for <see cref="SuspendTenantApplicationCommand"/>. Per-app kill
/// switch — does not affect sibling apps, the owning tenant, or its other
/// credentials (FR-AUTH-002 §5.6 BR4).
/// </summary>
public sealed class SuspendTenantApplicationCommandHandler
    : IRequestHandler<SuspendTenantApplicationCommand, Result<SuspendTenantApplicationResult>>
{
    private readonly ITenantApplicationRepository _applications;
    private readonly IUnitOfWork _uow;
    private readonly IActorProvider _actor;

    public SuspendTenantApplicationCommandHandler(
        ITenantApplicationRepository applications,
        IUnitOfWork uow,
        IActorProvider actor)
    {
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
    }

    public async Task<Result<SuspendTenantApplicationResult>> Handle(
        SuspendTenantApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await _applications
            .GetByIdAsync(request.TenantApplicationId, cancellationToken)
            .ConfigureAwait(false);

        if (application is null || application.TenantId != request.TenantId)
        {
            // Either the row does not exist, or it exists but belongs to a
            // different tenant. Same 404 either way — don't leak which
            // tenant owns the row to a caller that probed a foreign id.
            return Result<SuspendTenantApplicationResult>.Failure(
                ErrorCode.NotFound,
                $"tenant application {request.TenantApplicationId.Value:D} " +
                $"does not exist for tenant {request.TenantId.Value:D}.");
        }

        var actorId = _actor.CurrentActor();

        try
        {
            application.Suspend(actor: actorId);
        }
        catch (InvalidOperationException ex)
        {
            return Result<SuspendTenantApplicationResult>.Failure(
                ErrorCode.InvariantViolation,
                ex.Message);
        }

        await _applications.UpdateAsync(application, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<SuspendTenantApplicationResult>.Ok(new SuspendTenantApplicationResult(
            TenantApplicationId: application.Id,
            TenantId: application.TenantId,
            Platform: application.Platform,
            PackageId: application.PackageId,
            SuspendedAt: application.ModifiedAt ?? DateTimeOffset.UtcNow));
    }
}
