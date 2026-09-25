using MediatR;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.KeyCustody.Application.Queries.ListCryptoKeys;

/// <summary>
/// <c>GET /v1/crypto-keys</c> (no <c>TenantId</c> — lists across all
/// tenants) and <c>GET /v1/crypto-keys/{tenantId}</c> (scoped — the
/// tenant's full version history). 1-based paging, validated by
/// <see cref="ListCryptoKeysValidator"/>.
/// </summary>
public sealed record ListCryptoKeysQuery(
    Guid? TenantId,
    CryptoKeyStatus? Status,
    int Page,
    int PageSize) : IRequest<Result<PagedResult<CryptoKeySummary>>>;
