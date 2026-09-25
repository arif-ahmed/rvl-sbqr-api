using FluentValidation;

namespace SBQR.Modules.KeyCustody.Application.Queries.ListCryptoKeys;

/// <summary>
/// FluentValidation rules for <see cref="ListCryptoKeysQuery"/>. Bounds on
/// page and pageSize only — tenantId / status filters are nullable and have
/// no validation rules (a missing value means "no filter on this field").
/// </summary>
public sealed class ListCryptoKeysValidator : AbstractValidator<ListCryptoKeysQuery>
{
    /// <summary>Maximum items per page.</summary>
    public const int MaxPageSize = 100;

    public ListCryptoKeysValidator()
    {
        RuleFor(q => q.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("'Page' must be ≥ 1 (1-based paging).");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage($"'PageSize' must be between 1 and {MaxPageSize}.");
    }
}
