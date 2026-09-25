using FluentValidation;

namespace SBQR.Modules.IdentityAccess.Application.Commands.IssueToken;

/// <summary>
/// FluentValidation rules for <see cref="IssueClientCredentialsTokenCommand"/>.
/// Runs inside the Shared Kernel <c>ValidationBehavior&lt;,&gt;</c> pipeline —
/// the handler can assume all three fields are non-empty. Structural OAuth2
/// checks (unknown client, bad secret, wrong grant) are handler concerns:
/// they must surface as RFC 6749 error codes, not validation messages.
/// </summary>
public sealed class IssueClientCredentialsTokenValidator
    : AbstractValidator<IssueClientCredentialsTokenCommand>
{
    public IssueClientCredentialsTokenValidator()
    {
        RuleFor(c => c.GrantType)
            .NotEmpty();

        RuleFor(c => c.ClientId)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(c => c.ClientSecret)
            .NotEmpty()
            .MaximumLength(512);
    }
}
