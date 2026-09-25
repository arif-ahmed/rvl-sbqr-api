using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Commands.ReinstateTenantApplication;

/// <summary>
/// FluentValidation rules for <see cref="ReinstateTenantApplicationCommand"/>.
/// Mirrors <c>SuspendTenantApplicationValidator</c>.
/// </summary>
public sealed class ReinstateTenantApplicationValidator : AbstractValidator<ReinstateTenantApplicationCommand>
{
    public ReinstateTenantApplicationValidator()
    {
        RuleFor(c => c.TenantId)
            .NotEqual(default(Domain.Aggregates.TenantId))
            .WithMessage("'TenantId' must be a non-empty Guid.");

        RuleFor(c => c.TenantApplicationId)
            .NotEqual(default(Domain.Aggregates.TenantApplicationId))
            .WithMessage("'TenantApplicationId' must be a non-empty Guid.");
    }
}
