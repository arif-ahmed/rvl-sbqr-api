using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Commands.TerminateTenant;

/// <summary>
/// FluentValidation rules for <see cref="TerminateTenantCommand"/>. Runs inside
/// the Shared Kernel <c>ValidationBehavior&lt;,&gt;</c> pipeline — handlers can
/// assume validated input. Stateful rules (tenant exists, current state is
/// non-terminal) are NOT declared here; the aggregate's <c>Deactivate</c>
/// method enforces the legal-transition invariant, and the handler maps the
/// resulting exception to <see cref="SBQR.SharedKernel.Application.ErrorCode.InvariantViolation"/>.
/// <c>IValidator</c> rules must be pure.
/// </summary>
public sealed class TerminateTenantValidator : AbstractValidator<TerminateTenantCommand>
{
    /// <summary>
    /// DB column max length for the reason text we record in
    /// <c>audit_logs.metadata</c>. Same constant used by
    /// <c>SuspendTenantValidator</c> so the two endpoints stay symmetric.
    /// </summary>
    private const int ReasonMaxLength = 500;

    public TerminateTenantValidator()
    {
        RuleFor(c => c.TenantId)
            .NotEqual(default(Domain.Aggregates.TenantId))
            .WithMessage("'TenantId' must be a non-empty Guid.");

        RuleFor(c => c.Reason)
            .MaximumLength(ReasonMaxLength)
            .When(c => c.Reason is not null)
            .WithMessage($"'Reason' must be ≤ {ReasonMaxLength} characters when supplied.");
    }
}