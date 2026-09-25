using FluentValidation;

namespace SBQR.Modules.QrGeneration.Application.Commands;

/// <summary>
/// Recipient-identity fields common to both static and dynamic QR issuance.
/// Codec byte caps (spec Tables 3A/3B/4A) and cheap client feedback before
/// the build step. Included by both concrete validators below so the rules
/// are declared exactly once.
/// </summary>
internal interface IRecipientQrFields
{
    string RecipientName { get; }
    string RecipientCity { get; }
    string RecipientPan { get; }
    string? PostalCode { get; }
    string? CustomerLabel { get; }
    string? PurposeOfTransaction { get; }
    string? IdempotencyKey { get; }
}

/// <summary>
/// Recipient-identity field rules shared by both endpoints (codec byte
/// caps; cheap client feedback before the build step). Included by both
/// concrete validators below so the rules are declared exactly once.
/// </summary>
internal sealed class RecipientQrFieldsValidator : AbstractValidator<IRecipientQrFields>
{
    public RecipientQrFieldsValidator()
    {
        RuleFor(x => x.RecipientName).NotEmpty().MaximumLength(25);
        RuleFor(x => x.RecipientCity).NotEmpty().MaximumLength(15);
        RuleFor(x => x.RecipientPan).NotEmpty().MaximumLength(19);
        RuleFor(x => x.PostalCode).MaximumLength(10).When(x => x.PostalCode is not null);
        RuleFor(x => x.CustomerLabel).MaximumLength(25).When(x => x.CustomerLabel is not null);
        RuleFor(x => x.PurposeOfTransaction).MaximumLength(25).When(x => x.PurposeOfTransaction is not null);
        RuleFor(x => x.IdempotencyKey).MaximumLength(100).When(x => x.IdempotencyKey is not null);
    }
}
