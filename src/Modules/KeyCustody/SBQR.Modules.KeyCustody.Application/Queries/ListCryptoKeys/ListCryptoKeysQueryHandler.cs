using MediatR;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.Modules.KeyCustody.Domain.Interfaces;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.KeyCustody.Application.Queries.ListCryptoKeys;

/// <summary>Read-side handler for <see cref="ListCryptoKeysQuery"/>.</summary>
public sealed class ListCryptoKeysQueryHandler
    : IRequestHandler<ListCryptoKeysQuery, Result<PagedResult<CryptoKeySummary>>>
{
    private readonly ICryptoKeyRepository _keys;

    public ListCryptoKeysQueryHandler(ICryptoKeyRepository keys)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public async Task<Result<PagedResult<CryptoKeySummary>>> Handle(
        ListCryptoKeysQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (items, totalCount) = await _keys
            .ListAsync(request.TenantId, request.Status, request.Page, request.PageSize, cancellationToken)
            .ConfigureAwait(false);

        var projected = items.Select(CryptoKeySummaryBuilder.Build).ToList();

        return Result<PagedResult<CryptoKeySummary>>.Ok(new PagedResult<CryptoKeySummary>(
            Items: projected,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalCount: totalCount));
    }
}
