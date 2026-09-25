using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Commands.ActivateTenant;

/// <summary>
/// FluentValidation rules for <see cref="ActivateTenantCommand"/>. Stateful
/// rules (e.g. "tenant must be Pending or Suspended") are NOT declared here —
/// the aggregate's <c>Activate</c> method enforces the legal-transition
/// invariant, and the handler maps the resulting exception to
/// <see cref="SBQR.SharedKernel.Application.ErrorCode.InvariantViolation"/>.
/// <c>IValidator</c> rules must be pure.
/// </summary>
public sealed class ActivateTenantValidator : AbstractValidator<ActivateTenantCommand>
{
    public ActivateTenantValidator()
    {
        RuleFor(c => c.TenantId)
            .NotEqual(default(SBQR.Modules.Tenancy.Domain.Aggregates.TenantId));
    }
}