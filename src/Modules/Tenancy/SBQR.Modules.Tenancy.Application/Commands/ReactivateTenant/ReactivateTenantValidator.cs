using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Commands.ReactivateTenant;

/// <summary>
/// FluentValidation rules for <see cref="ReactivateTenantCommand"/>. Runs
/// inside the Shared Kernel <c>ValidationBehavior&lt;,&gt;</c> pipeline.
/// Stateful rules (tenant exists, current state is Suspended) are NOT
/// declared here; the handler enforces them via the repository pre-check,
/// because <c>IValidator</c> rules must be pure.
/// </summary>
public sealed class ReactivateTenantValidator : AbstractValidator<ReactivateTenantCommand>
{
    public ReactivateTenantValidator()
    {
        // TenantId is a strongly-typed record struct — Guid.Empty is the only
        // invalid value and we reject it so the handler doesn't have to.
        RuleFor(c => c.TenantId)
            .NotEqual(default(Domain.Aggregates.TenantId))
            .WithMessage("'TenantId' must be a non-empty Guid.");
    }
}
