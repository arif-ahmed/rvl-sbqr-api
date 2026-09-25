using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Commands.RegisterTenantApplication;

/// <summary>
/// FluentValidation rules for <see cref="RegisterTenantApplicationCommand"/>.
/// Runs inside the Shared Kernel <c>ValidationBehavior&lt;,&gt;</c> pipeline
/// — handlers can assume validated input. Stateful rules (tenant exists, no
/// duplicate (platform, package_id)) are NOT declared here; the DB FK +
/// UNIQUE index are the authoritative guards, because <c>IValidator</c> rules
/// must be pure.
/// </summary>
public sealed class RegisterTenantApplicationValidator : AbstractValidator<RegisterTenantApplicationCommand>
{
    /// <summary>DB column max length for <c>tenant_applications.package_id</c>.</summary>
    private const int PackageIdMaxLength = 200;

    /// <summary>
    /// Reverse-DNS pattern used by both Android <c>applicationId</c> and iOS
    /// bundle identifiers. Not all real-world identifiers match this pattern
    /// in every store, but the platform-published grammar is reverse-DNS;
    /// we keep the surface tight and let the admin override per-store
    /// exceptions manually through the API when needed.
    /// </summary>
    private const string PackageIdPattern = "^[A-Za-z][A-Za-z0-9_]*(\\.[A-Za-z0-9_]+)+$";

    public RegisterTenantApplicationValidator()
    {
        RuleFor(c => c.TenantId)
            .NotEqual(default(Domain.Aggregates.TenantId))
            .WithMessage("'TenantId' must be a non-empty Guid.");

        RuleFor(c => c.PackageId)
            .NotEmpty()
            .MaximumLength(PackageIdMaxLength)
            .Matches(PackageIdPattern)
            .WithMessage(
                $"'{{PropertyName}}' must be a reverse-DNS identifier " +
                $"(≤ {PackageIdMaxLength} characters) matching {PackageIdPattern}.");
    }
}
