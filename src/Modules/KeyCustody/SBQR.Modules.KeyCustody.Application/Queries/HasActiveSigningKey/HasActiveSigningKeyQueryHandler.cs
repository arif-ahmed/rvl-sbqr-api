using MediatR;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.KeyCustody.Domain.Interfaces;

namespace SBQR.Modules.KeyCustody.Application.Queries.HasActiveSigningKey;

/// <summary>
/// Resolves <see cref="HasActiveSigningKeyQuery"/>: returns <c>true</c>
/// when the tenant has at least one ACTIVE row in <c>crypto_keys</c>.
/// Pure read; no audit row, no state mutation. Used by the Tenancy
/// activate-gate (FR-TENANT-001).
///
/// <para>
/// Backed by the same <c>GetActiveByTenantAsync</c> repository method
/// <see cref="SBQR.Modules.KeyCustody.Application.Queries.GetActiveCryptoKey.GetActiveCryptoKeyQueryHandler"/>
/// uses — runs at most one indexed lookup, no scan.
/// </para>
/// </summary>
public sealed class HasActiveSigningKeyQueryHandler
    : IRequestHandler<HasActiveSigningKeyQuery, bool>
{
    private readonly ICryptoKeyRepository _keys;

    public HasActiveSigningKeyQueryHandler(ICryptoKeyRepository keys)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public async Task<bool> Handle(
        HasActiveSigningKeyQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = await _keys
            .GetActiveByTenantAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return key is not null;
    }
}
