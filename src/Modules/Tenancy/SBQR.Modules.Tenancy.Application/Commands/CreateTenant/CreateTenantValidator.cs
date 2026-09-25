using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Commands.CreateTenant;

/// <summary>
/// FluentValidation rules for <see cref="CreateTenantCommand"/>. Runs inside the
/// Shared Kernel <c>ValidationBehavior&lt;,&gt;</c> pipeline — handlers can assume
/// validated input. Stateful rules (uniqueness of <c>institution_code</c>) are
/// NOT declared here; the DB UNIQUE constraint is the authoritative guard,
/// because <c>IValidator</c> rules must be pure.
/// </summary>
public sealed class CreateTenantValidator : AbstractValidator<CreateTenantCommand>
{
    /// <summary>Bangladesh Bank institution code format: type(2) ‖ id(4), six digits.</summary>
    private const string InstitutionCodePattern = "^[0-9]{6}$";

    public CreateTenantValidator()
    {
        RuleFor(c => c.InstitutionName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(c => c.InstitutionCode)
            .NotEmpty()
            .Matches(InstitutionCodePattern)
            .WithMessage($"'{{PropertyName}}' must match {InstitutionCodePattern}.");
    }
}