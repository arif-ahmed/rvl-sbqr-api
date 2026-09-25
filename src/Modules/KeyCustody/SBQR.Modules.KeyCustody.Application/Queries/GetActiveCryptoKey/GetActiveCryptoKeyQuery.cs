using MediatR;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.KeyCustody.Application.Queries.GetActiveCryptoKey;

/// <summary>
/// <c>GET /v1/crypto-keys/{tenantId}/active</c> — the tenant's currently
/// ACTIVE signing key. Fails with <see cref="ErrorCode.NotFound"/> when the
/// tenant has no ACTIVE key (whether or not it has other, non-active rows).
/// </summary>
public sealed record GetActiveCryptoKeyQuery(Guid TenantId) : IRequest<Result<CryptoKeySummary>>;
