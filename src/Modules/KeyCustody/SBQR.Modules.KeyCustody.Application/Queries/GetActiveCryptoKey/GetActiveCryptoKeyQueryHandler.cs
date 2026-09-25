using MediatR;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.Modules.KeyCustody.Domain.Interfaces;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.KeyCustody.Application.Queries.GetActiveCryptoKey;

/// <summary>Read-side handler for <see cref="GetActiveCryptoKeyQuery"/>.</summary>
public sealed class GetActiveCryptoKeyQueryHandler
    : IRequestHandler<GetActiveCryptoKeyQuery, Result<CryptoKeySummary>>
{
    private readonly ICryptoKeyRepository _keys;

    public GetActiveCryptoKeyQueryHandler(ICryptoKeyRepository keys)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public async Task<Result<CryptoKeySummary>> Handle(
        GetActiveCryptoKeyQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = await _keys
            .GetActiveByTenantAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return key is null
            ? Result<CryptoKeySummary>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId:D} has no ACTIVE signing key.")
            : Result<CryptoKeySummary>.Ok(CryptoKeySummaryBuilder.Build(key));
    }
}
