using FluentValidation;

namespace SBQR.Modules.QrGeneration.Application.Commands.GenerateStaticQr;

/// <summary>
/// Recipient-identity rules for the static endpoint. The shared
/// <see cref="RecipientQrFieldsValidator"/> carries the byte caps; no
/// static-specific rule is needed beyond that because Tag 54 is
/// structurally absent from the static command (compile-time invariant).
/// </summary>
public sealed class GenerateStaticQrValidator : AbstractValidator<GenerateStaticQrCommand>
{
    public GenerateStaticQrValidator()
    {
        Include(new RecipientQrFieldsValidator());
    }
}
