using FluentValidation;

namespace SBQR.Modules.QrGeneration.Application.Commands.GenerateDynamicQr;

/// <summary>
/// Recipient-identity rules plus the dynamic-only Tag 54 (Transaction
/// Amount) rule. Spec Table 3A: Tag 54 is <c>var. up to "13"</c>, must be
/// digits with an optional single decimal point. The rule is declared once
/// here; the codec enforces it again as a defense in depth.
/// </summary>
public sealed class GenerateDynamicQrValidator : AbstractValidator<GenerateDynamicQrCommand>
{
    public GenerateDynamicQrValidator()
    {
        Include(new RecipientQrFieldsValidator());
        RuleFor(x => x.TransactionAmount).NotEmpty().MaximumLength(13)
            .Matches(@"^[0-9]+(\.[0-9]+)?$")
            .WithMessage("Transaction Amount must be digits with an optional single decimal point, up to 13 bytes.");
    }
}
