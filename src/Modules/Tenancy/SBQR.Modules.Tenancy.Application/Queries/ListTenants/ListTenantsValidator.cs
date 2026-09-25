using FluentValidation;

namespace SBQR.Modules.Tenancy.Application.Queries.ListTenants;

/// <summary>
/// FluentValidation rules for <see cref="ListTenantsQuery"/>. Bounds on page
/// and pageSize only — status / isActive filters are nullable and have no
/// validation rules (a missing value means "no filter on this field").
/// </summary>
public sealed class ListTenantsValidator : AbstractValidator<ListTenantsQuery>
{
    /// <summary>
    /// Maximum items per page. Keeps the admin-console from accidentally
    /// pulling the entire tenants table into memory in one request.
    /// </summary>
    public const int MaxPageSize = 100;

    public ListTenantsValidator()
    {
        RuleFor(q => q.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("'Page' must be ≥ 1 (1-based paging).");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, MaxPageSize)
            .WithMessage($"'PageSize' must be between 1 and {MaxPageSize}.");
    }
}