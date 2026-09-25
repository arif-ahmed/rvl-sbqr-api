using MediatR;
using Microsoft.EntityFrameworkCore;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.InstitutionTrust.Infrastructure.Persistence;

namespace SBQR.Modules.InstitutionTrust.Application.Queries;

/// <summary>
/// Resolves a public key for an institution from the trust directory.
///
/// <para>
/// Spec Annex B step 2: "retrieve the corresponding public key file from
/// the BB Trust Store using the derived Institution_ID." This handler is
/// the in-process implementation of that store lookup — it has no
/// KeyCustody dependency and no own-custody branch. Verification calls
/// this for every issuer, tenant-owned or external.
/// </para>
///
/// <para>
/// <b>Temporal validity (C16 gate):</b> <c>ACTIVE</c> keys are returned
/// only when <c>ValidFrom ≤ now</c> and (<c>ValidTo IS NULL OR ValidTo &gt; now</c>)
/// and <c>RevokedAt IS NULL</c>. A revoked key (<c>REVOKED</c> status) is
/// never returned as an activator; the caller receives <c>null</c> and
/// produces a <c>KEY_REVOKED</c> verdict by re-querying with status
/// inspection.
/// </para>
///
/// <para>
/// <b>Historical key fallback (Phase 4):</b> when
/// <see cref="GetInstitutionPublicKeyQuery.IncludeHistorical"/> is
/// <c>true</c>, the query returns the highest-version <c>RETIRED</c> key
/// that was valid at some point (ValidFrom ≤ now). This lets
/// Verification retry signature verification against a previous key
/// version after key rotation — a structurally valid, correctly signed QR
/// issued before rotation would otherwise fail forever.
/// </para>
///
/// <para>
/// <b>Status transparency:</b> the returned <see cref="InstitutionPublicKeyView"/>
/// carries the raw status string so Verification can map it to the
/// appropriate verdict (<c>SUSPENDED</c> → <c>KEY_SUSPENDED</c>,
/// <c>REVOKED</c> → <c>KEY_REVOKED</c>).
/// </para>
/// </summary>
public sealed class GetInstitutionPublicKeyQueryHandler
    : IRequestHandler<GetInstitutionPublicKeyQuery, InstitutionPublicKeyView?>
{
    private readonly InstitutionTrustDbContext _db;

    public GetInstitutionPublicKeyQueryHandler(InstitutionTrustDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<InstitutionPublicKeyView?> Handle(
        GetInstitutionPublicKeyQuery request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var query = _db.Keys
            .IgnoreQueryFilters()
            .Where(k => k.InstitutionCode == request.InstitutionCode);

        if (request.IncludeHistorical)
        {
            // Fallback: only RETIRED keys that pass temporal validity.
            // REVOKED keys are never returned — fail-closed on compromise.
            query = query.Where(k => k.Status == "RETIRED"
                && k.ValidFrom <= now
                && k.RevokedAt == null);
        }
        else
        {
            // Primary: ACTIVE keys must pass the C16 temporal gate.
            query = query.Where(k => k.Status == "ACTIVE"
                && k.ValidFrom <= now
                && (k.ValidTo == null || k.ValidTo > now)
                && k.RevokedAt == null);
        }

        var key = await query
            .OrderByDescending(k => k.KeyVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return key is null
            ? null
            : new InstitutionPublicKeyView(
                InstitutionCode: key.InstitutionCode,
                KeyVersion: key.KeyVersion,
                PublicKeyPem: key.PublicKey,
                Status: key.Status);
    }
}
