using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Commands.ProvisionTenantConfiguration;

/// <summary>
/// FluentValidation rules for <see cref="ProvisionTenantConfigurationCommand"/>.
/// Runs inside the Shared Kernel <c>ValidationBehavior&lt;,&gt;</c> pipeline —
/// handlers can assume validated input. Stateful rules (tenant exists, is not
/// Suspended / Terminated, has no active configuration) are NOT declared here;
/// the handler enforces them via the repository pre-check and the
/// provisioner's own single-active-configuration guard, because
/// <c>IValidator</c> rules must be pure.
/// </summary>
public sealed class ProvisionTenantConfigurationValidator : AbstractValidator<ProvisionTenantConfigurationCommand>
{
    public ProvisionTenantConfigurationValidator()
    {
        // TenantId is a strongly-typed record struct — Guid.Empty is the only
        // invalid value and we reject it so the handler doesn't have to.
        RuleFor(c => c.TenantId)
            .NotEqual(default(Domain.Aggregates.TenantId))
            .WithMessage("'TenantId' must be a non-empty Guid.");

        // "At least one QR capability" — a configuration row minted with both
        // isQrGenerationAllowed=false AND isQrValidationAllowed=false
        // authorises zero QR flows, so the credential can never be used at
        // the call site. The post-issuance flip verbs (Disable / Enable on
        // TenantConfiguration) are how an admin takes a tenant's last
        // capability away later.
        RuleFor(c => c)
            .Must(c => c.IsQrGenerationAllowed || c.IsQrValidationAllowed)
            .WithMessage("At least one of 'isQrGenerationAllowed' or 'isQrValidationAllowed' must be true.");
    }
}
