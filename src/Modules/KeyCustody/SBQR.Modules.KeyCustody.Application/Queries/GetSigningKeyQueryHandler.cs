using MediatR;
using Microsoft.EntityFrameworkCore;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.KeyCustody.Infrastructure.Persistence;

namespace SBQR.Modules.KeyCustody.Application.Queries;

/// <summary>
/// Resolves <see cref="GetSigningKeyQuery"/> from the <c>crypto_keys</c>
/// table: the tenant's highest-version live row, any status.
/// </summary>
public sealed class GetSigningKeyQueryHandler
    : IRequestHandler<GetSigningKeyQuery, SigningKeyView?>
{
    private readonly KeyCustodyDbContext _db;

    public GetSigningKeyQueryHandler(KeyCustodyDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<SigningKeyView?> Handle(GetSigningKeyQuery request, CancellationToken cancellationToken)
    {
        var record = await _db.CryptoKeys
            .Where(k => k.TenantId == request.TenantId)
            .OrderByDescending(k => k.KeyVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return record is null
            ? null
            : new SigningKeyView(
                CryptoKeyId: record.Id.Value,
                TenantId: record.TenantId,
                KeyId: record.KeyId,
                KeyVersion: record.KeyVersion,
                PublicKeyPem: record.PublicKey,
                Status: record.Status.ToString().ToUpperInvariant());
    }
}
