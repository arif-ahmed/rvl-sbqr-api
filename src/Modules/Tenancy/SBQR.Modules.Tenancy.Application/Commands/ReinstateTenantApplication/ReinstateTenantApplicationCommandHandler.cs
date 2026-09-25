using MediatR;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Application.Commands.ReinstateTenantApplication;

/// <summary>
/// Handler for <see cref="ReinstateTenantApplicationCommand"/>. Counterpart to
/// <c>SuspendTenantApplicationCommandHandler</c> — undoes the per-app kill
/// switch and brings the row back to <c>ACTIVE</c>.
/// </summary>
public sealed class ReinstateTenantApplicationCommandHandler
    : IRequestHandler<ReinstateTenantApplicationCommand, Result<ReinstateTenantApplicationResult>>
{
    private readonly ITenantApplicationRepository _applications;
    private readonly IUnitOfWork _uow;
    private readonly IActorProvider _actor;

    public ReinstateTenantApplicationCommandHandler(
        ITenantApplicationRepository applications,
        IUnitOfWork uow,
        IActorProvider actor)
    {
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
    }

    public async Task<Result<ReinstateTenantApplicationResult>> Handle(
        ReinstateTenantApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await _applications
            .GetByIdAsync(request.TenantApplicationId, cancellationToken)
            .ConfigureAwait(false);

        if (application is null || application.TenantId != request.TenantId)
        {
            return Result<ReinstateTenantApplicationResult>.Failure(
                ErrorCode.NotFound,
                $"tenant application {request.TenantApplicationId.Value:D} " +
                $"does not exist for tenant {request.TenantId.Value:D}.");
        }

        var actorId = _actor.CurrentActor();

        try
        {
            application.Reinstate(actor: actorId);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ReinstateTenantApplicationResult>.Failure(
                ErrorCode.InvariantViolation,
                ex.Message);
        }

        await _applications.UpdateAsync(application, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ReinstateTenantApplicationResult>.Ok(new ReinstateTenantApplicationResult(
            TenantApplicationId: application.Id,
            TenantId: application.TenantId,
            Platform: application.Platform,
            PackageId: application.PackageId,
            ReinstatedAt: application.ModifiedAt ?? DateTimeOffset.UtcNow));
    }
}
