using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Commands.SuspendTenant;

/// <summary>
/// FluentValidation rules for <see cref="SuspendTenantCommand"/>. Runs inside
/// the Shared Kernel <c>ValidationBehavior&lt;,&gt;</c> pipeline — handlers can
/// assume validated input. Stateful rules (tenant exists, current state is
/// non-terminal &amp; non-suspended) are NOT declared here; the handler enforces
/// them via the repository pre-check, because <c>IValidator</c> rules must be
/// pure.
/// </summary>
public sealed class SuspendTenantValidator : AbstractValidator<SuspendTenantCommand>
{
    /// <summary>
    /// DB column max length for the reason text we record in
    /// <c>audit_logs.metadata</c>. Keeping the rule here is cheaper than letting
    /// the DB truncate; the same constant is referenced from the handler's audit
    /// payload formatting.
    /// </summary>
    private const int ReasonMaxLength = 500;

    public SuspendTenantValidator()
    {
        // TenantId is a strongly-typed record struct — Guid.Empty is the only
        // invalid value and we reject it so the handler doesn't have to.
        RuleFor(c => c.TenantId)
            .NotEqual(default(Domain.Aggregates.TenantId))
            .WithMessage("'TenantId' must be a non-empty Guid.");

        RuleFor(c => c.Reason)
            .MaximumLength(ReasonMaxLength)
            .When(c => c.Reason is not null)
            .WithMessage($"'Reason' must be ≤ {ReasonMaxLength} characters when supplied.");
    }
}
