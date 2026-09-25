using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Commands.SuspendTenantApplication;

/// <summary>
/// FluentValidation rules for <see cref="SuspendTenantApplicationCommand"/>.
/// Runs inside the Shared Kernel <c>ValidationBehavior&lt;,&gt;</c> pipeline.
/// </summary>
public sealed class SuspendTenantApplicationValidator : AbstractValidator<SuspendTenantApplicationCommand>
{
    public SuspendTenantApplicationValidator()
    {
        RuleFor(c => c.TenantId)
            .NotEqual(default(Domain.Aggregates.TenantId))
            .WithMessage("'TenantId' must be a non-empty Guid.");

        RuleFor(c => c.TenantApplicationId)
            .NotEqual(default(Domain.Aggregates.TenantApplicationId))
            .WithMessage("'TenantApplicationId' must be a non-empty Guid.");
    }
}
