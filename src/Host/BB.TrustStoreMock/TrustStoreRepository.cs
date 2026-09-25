// src/BB.TrustStoreMock/TrustStoreRepository.cs
using System.Security.Cryptography;
using System.Text;
using BB.TrustStoreMock.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BB.TrustStoreMock;

/// <summary>
/// Immutable read-model snapshots handed to the controllers. Mirrors the
/// shape the old in-memory store exposed, so the HTTP surface is unchanged.
/// </summary>
public sealed record KeySnapshot(
    int KeyVersion,
    string PublicKeyPem,
    string Sha256,
    string Status,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo);

public sealed record InstitutionSnapshot(
    string InstitutionId,
    string InstituteType,
    string InstitutionName,
    string Status,
    IReadOnlyList<KeySnapshot> Keys)
{
    /// <summary>The currently trusted key, or null if there is none (e.g. after a revocation).</summary>
    public KeySnapshot? ActiveKey =>
        Keys.Where(k => k.Status == "ACTIVE").OrderByDescending(k => k.KeyVersion).FirstOrDefault();

    /// <summary>
    /// The latest key WHATEVER its status. The list endpoint reports this
    /// so a revoked institution appears in the feed with a REVOKED key
    /// rather than silently vanishing — revocation must be explicit, never
    /// inferred from absence.
    /// </summary>
    public KeySnapshot? MostRecentKey =>
        Keys.Count == 0 ? null : Keys.MaxBy(k => k.KeyVersion);
}

/// <summary>Outcome of an upload: whether a NEW key version was minted.</summary>
public sealed record UpsertResult(InstitutionSnapshot Snapshot, bool NewKeyVersion);

/// <summary>
/// Database-backed trust store for the mock. Owns the semantics that the
/// old <c>InMemoryTrustStore</c> carried, plus the hardening the mock
/// needed:
///
/// <list type="bullet">
///   <item><b>Idempotent upload</b> — re-uploading the SAME public key is a
///   no-op on key state (metadata-only), so a re-publish or a sync loop
///   cannot inflate key versions.</item>
///   <item><b>Validity windows</b> — optional validFrom/validTo stored and
///   echoed verbatim; never used to derive status here.</item>
///   <item><b>Explicit revocation</b> — ACTIVE → REVOKED only via
///   <see cref="RevokeActiveKeyAsync"/>; the list feed keeps reporting the
///   institution so a revocation can propagate to consumers.</item>
/// </list>
///
/// Concurrency is dev-grade: a DB transaction plus the unique
/// (institution_id, key_version) index keep versioning sane; the mock sees
/// only single-digit dev/test traffic.
/// </summary>
public sealed class TrustStoreRepository(TrustStoreDbContext db)
{
    public async Task<InstitutionSnapshot?> GetAsync(string institutionId, CancellationToken ct)
    {
        var row = await LoadAsync(institutionId, ct).ConfigureAwait(false);
        return row is null ? null : Snapshot(row);
    }

    public async Task<IReadOnlyList<InstitutionSnapshot>> GetAllAsync(CancellationToken ct)
    {
        var rows = await db.Institutions
            .Include(i => i.Keys)
            .AsNoTracking()
            .OrderBy(i => i.InstitutionId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.Select(Snapshot).ToList();
    }

    public async Task<UpsertResult> UpsertAsync(
        string institutionId,
        string institutionName,
        string publicKeyPem,
        string? instituteType,
        DateTimeOffset? validFrom,
        DateTimeOffset? validTo,
        CancellationToken ct)
    {
        // Annex A Institution Type (Tag 26 sub 01) — the InstitutionId prefix
        // when the caller omits it (validator guarantees 6-digit id + prefix
        // match when supplied).
        var type = string.IsNullOrWhiteSpace(instituteType)
            ? institutionId[..2]
            : instituteType.Trim();
        var sha256 = Sha256OfPem(publicKeyPem);
        var now = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var institution = await LoadAsync(institutionId, ct).ConfigureAwait(false);
        if (institution is null)
        {
            institution = new InstitutionRow
            {
                InstitutionId = institutionId,
                InstituteType = type,
                InstitutionName = institutionName,
            };
            db.Institutions.Add(institution);
        }

        institution.InstitutionName = institutionName;
        institution.InstituteType = type;

        var active = institution.Keys
            .Where(k => k.Status == "ACTIVE")
            .OrderByDescending(k => k.KeyVersion)
            .FirstOrDefault();

        bool newKeyVersion;
        if (active is not null && active.Sha256 == sha256)
        {
            // Idempotent re-publish: same key material. Refresh metadata
            // only — no retirement, no version bump. This is what keeps a
            // periodic sync from minting a new version of an unchanged key.
            newKeyVersion = false;
        }
        else
        {
            foreach (var existing in institution.Keys.Where(k => k.Status == "ACTIVE"))
            {
                existing.Status = "RETIRED";
            }

            var nextVersion = institution.Keys.Count == 0
                ? 1
                : institution.Keys.Max(k => k.KeyVersion) + 1;

            institution.Keys.Add(new KeyRow
            {
                InstitutionId = institutionId,
                KeyVersion = nextVersion,
                PublicKeyPem = publicKeyPem,
                Sha256 = sha256,
                Status = "ACTIVE",
                ValidFromUtc = (validFrom?.UtcDateTime) ?? now,
                ValidToUtc = validTo?.UtcDateTime,
                CreatedAtUtc = now,
            });

            newKeyVersion = true;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        return new UpsertResult(Snapshot(institution), newKeyVersion);
    }

    /// <summary>
    /// Marks the institution's active key(s) REVOKED — exercises the "key
    /// revoked" rejection path from Annex B Step 3. Returns false when the
    /// institution is unknown (caller maps that to 404).
    /// </summary>
    public async Task<bool> RevokeActiveKeyAsync(string institutionId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var institution = await LoadAsync(institutionId, ct).ConfigureAwait(false);
        if (institution is null)
        {
            return false;
        }

        var revokedAny = false;
        foreach (var key in institution.Keys.Where(k => k.Status == "ACTIVE"))
        {
            key.Status = "REVOKED";
            revokedAny = true;
        }

        if (revokedAny)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<InstitutionRow?> LoadAsync(string institutionId, CancellationToken ct) =>
        await db.Institutions
            .Include(i => i.Keys)
            .FirstOrDefaultAsync(i => i.InstitutionId == institutionId, ct)
            .ConfigureAwait(false);

    private static InstitutionSnapshot Snapshot(InstitutionRow row)
    {
        // Institution-level status is DERIVED, never stored: ACTIVE while
        // the institution has an ACTIVE key, INACTIVE once revoked/retired.
        // (The old in-memory store left it "ACTIVE" forever — inconsistent
        // with the key it was reporting.)
        var keys = row.Keys
            .OrderBy(k => k.KeyVersion)
            .Select(k => new KeySnapshot(
                k.KeyVersion,
                k.PublicKeyPem,
                k.Sha256,
                k.Status,
                new DateTimeOffset(DateTime.SpecifyKind(k.ValidFromUtc, DateTimeKind.Utc), TimeSpan.Zero),
                k.ValidToUtc is null
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(k.ValidToUtc.Value, DateTimeKind.Utc), TimeSpan.Zero)))
            .ToList();

        var status = keys.Any(k => k.Status == "ACTIVE") ? "ACTIVE" : "INACTIVE";

        return new InstitutionSnapshot(row.InstitutionId, row.InstituteType, row.InstitutionName, status, keys);
    }

    internal static string Sha256OfPem(string pem) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(pem))).ToLowerInvariant();
}
