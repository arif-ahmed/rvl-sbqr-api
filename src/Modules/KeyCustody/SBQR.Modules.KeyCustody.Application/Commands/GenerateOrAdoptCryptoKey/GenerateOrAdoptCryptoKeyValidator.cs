using FluentValidation;

namespace SBQR.Modules.KeyCustody.Application.Commands.GenerateOrAdoptCryptoKey;

/// <summary>
/// FluentValidation rules for <see cref="GenerateOrAdoptCryptoKeyCommand"/>.
/// Stateful rules (tenant exists, no ACTIVE key already) are NOT declared
/// here — the handler enforces them via repository pre-checks, because
/// <c>IValidator</c> rules must be pure.
/// </summary>
public sealed class GenerateOrAdoptCryptoKeyValidator : AbstractValidator<GenerateOrAdoptCryptoKeyCommand>
{
    public GenerateOrAdoptCryptoKeyValidator()
    {
        RuleFor(c => c.TenantId)
            .NotEqual(Guid.Empty)
            .WithMessage("'TenantId' must be a non-empty Guid.");

        RuleFor(c => c.Mode)
            .IsInEnum();

        When(c => c.Mode == CryptoKeyMode.Adopt, () =>
        {
            // Adopt mode: only the private-key half is supplied in the
            // request body. The public half is read from
            // public.institution_keys via the drift guard; the
            // operator pre-seeds that row via POST /v1/admin/institutions
            // before invoking Adopt.
            RuleFor(c => c.PrivateKeyPem)
                .NotEmpty()
                .WithMessage("'PrivateKeyPem' is required when Mode is 'Adopt'.");
        });
    }
}
