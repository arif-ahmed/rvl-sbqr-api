using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SBQR.Modules.InstitutionTrust.Application.Commands;
using SBQR.Modules.InstitutionTrust.Domain.Persistence;
using SBQR.Modules.InstitutionTrust.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.InstitutionTrust.Application.Services;

/// <summary>
/// The one code path that writes to <c>institution_keys</c>: register-or-update
/// an institution and publish an SPKI public-key PEM as its new ACTIVE key
/// version, retiring the previous active key. Used by both the manual admin
/// upsert (<see cref="Commands.UpsertInstitutionCommand"/>) and
/// <c>DailyTrustSyncService</c>, so "how an institution/key gets written" has
/// exactly one implementation regardless of which caller triggered it.
///
/// <para>
/// <b>Identity</b> (institution code → display name + Annex A Type) is
/// denormalised onto this row in <c>institution_name</c> /
/// <c>institute_type</c> so a verifier resolving a QR against this
/// directory gets the issuer's identity without a join onto
/// <c>public.tenants</c>. The DB-level <c>ck_institution_keys_name_nonblank</c>
/// and <c>ck_institution_keys_institute_type</c> CHECK constraints reject
/// blanks/malformed values; the validator here enforces the same rules at
/// the application boundary.
/// </para>
///
/// <see cref="RetireKeyAsync"/> is the counterpart for explicit revocation:
/// the trust store reported a non-ACTIVE key, so the local ACTIVE key is
/// retired and no replacement is published.
///
/// <para>
/// Every write path runs the shared shape guard
/// (<see cref="TrustRecordPatterns"/>) first and throws
/// <see cref="InvalidTrustStoreRecordException"/> on a malformed record, so
/// bad trust-store data never reaches the database CHECK constraints.
/// </para>
/// </summary>
public sealed class InstitutionUpsertService
{
    private readonly InstitutionTrustDbContext _db;
    private readonly IAuditLogger _audit;

    public InstitutionUpsertService(InstitutionTrustDbContext db, IAuditLogger audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <summary>
    /// Publish a new ACTIVE key version for the institution. Retires the
    /// current ACTIVE row in the same transaction; bumps <c>key_version</c>
    /// monotonically; records <c>source</c>, <c>valid_from</c>, the
    /// <c>institution_name</c> + <c>institute_type</c> denormalised onto
    /// the row, and the audit-row actor.
    /// </summary>
    /// <param name="institutionCode">Six-digit BB institution code.</param>
    /// <param name="instituteType">Two-digit Annex A Institution Type
    /// (Tag 26 sub 01 — the BB InstitutionId prefix). Normally derivable
    /// from <paramref name="institutionCode"/>[..2], but callers pass the
    /// BB-published value explicitly so the trust-store is the source of
    /// truth.</param>
    /// <param name="institutionName">Human-readable display name (NOT NULL
    /// on the row — the validator rejects blanks).</param>
    /// <param name="publicKeyPem">SPKI Ed25519 PEM.</param>
    /// <param name="actorId">Audit actor tag (manual admin uses a client id,
    /// system flows use <c>system:trust-sync</c> / <c>system:crypto-create</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UpsertedInstitution> UpsertAsync(
        string institutionCode,
        string instituteType,
        string institutionName,
        string publicKeyPem,
        string actorId,
        CancellationToken cancellationToken)
    {
        var code = (institutionCode ?? string.Empty).Trim();
        var type = (instituteType ?? string.Empty).Trim();
        var name = (institutionName ?? string.Empty).Trim();
        var pem = (publicKeyPem ?? string.Empty).Trim();

        Validate(code, type, name, pem);

        var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.ASCII.GetBytes(pem)))
            .ToLowerInvariant();

        var now = DateTimeOffset.UtcNow;
        var activeKeys = await _db.Keys
            .Where(k => k.InstitutionCode == code && k.Status == "ACTIVE")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Unchanged-record short-circuit: re-publishing the SAME key (the
        // common case on every sync tick once the directory is current)
        // must not retire-and-reinsert, or a periodic sync would inflate
        // key versions forever. Refresh the denormalised identity fields
        // and the sync timestamp on the existing ACTIVE row and return —
        // no new version, no churn audit.
        var currentActive = activeKeys
            .OrderByDescending(k => k.KeyVersion)
            .FirstOrDefault();
        if (currentActive is not null &&
            currentActive.PublicKeySha256 == fingerprint)
        {
            currentActive.InstitutionName = name;
            currentActive.InstituteType = type;
            currentActive.SyncedAt = now;
            currentActive.ModifiedAt = now;
            currentActive.ModifiedBy = actorId;

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new UpsertedInstitution(
                InstitutionCode: code,
                InstituteType: type,
                InstitutionName: name,
                ActiveKeyVersion: currentActive.KeyVersion,
                PublicKeySha256: fingerprint);
        }

        // Retire every currently ACTIVE row for this institution so the new
        // version supersedes them in one transaction. Supersession closes
        // the validity window (valid_to = now) but does NOT set revoked_at —
        // retirement by a newer key version is not a revocation event from
        // the trust store. The C4/C16 gate relies on `valid_to < now` to
        // reject an old version, so closing the window is the right move.
        foreach (var existing in activeKeys)
        {
            existing.Status = "RETIRED";
            existing.ValidTo = now;
            existing.ModifiedBy = actorId;
            existing.ModifiedAt = now;
        }

        var nextVersion = (await _db.Keys
            .IgnoreQueryFilters()
            .Where(k => k.InstitutionCode == code)
            .MaxAsync(k => (int?)k.KeyVersion, cancellationToken)
            .ConfigureAwait(false) ?? 0) + 1;

        _db.Keys.Add(new InstitutionKey
        {
            InstitutionKeyId = Guid.NewGuid(),
            InstitutionCode = code,
            InstituteType = type,
            InstitutionName = name,
            KeyVersion = nextVersion,
            PublicKey = pem,
            PublicKeySha256 = fingerprint,
            // LOCAL for our own tenant publish (manual admin or auto-publish
            // from KeyCustody). The trust-store sync overwrites this to
            // REGISTRY on its branch in DailyTrustSyncService (same DB
            // shape, different source value).
            Source = "LOCAL",
            Status = "ACTIVE",
            IsActive = true,
            ValidFrom = now,
            ValidTo = null,
            RevokedAt = null,
            SyncedAt = now,
            CreatedBy = actorId,
            CreatedAt = now,
            ModifiedBy = null,
            ModifiedAt = null,
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.LogAsync(new AuditEntry(
            Action: "institution.trust.key.published",
            ActorId: actorId,
            ResourceType: "institution",
            ResourceId: code,
            Metadata: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["active_key_version"] = nextVersion,
                ["public_key_sha256"] = fingerprint,
                ["institute_type"] = type,
                ["institution_name"] = name,
                ["retired_versions"] = activeKeys.Select(k => k.KeyVersion).ToList(),
            })),
            cancellationToken).ConfigureAwait(false);

        return new UpsertedInstitution(
            InstitutionCode: code,
            InstituteType: type,
            InstitutionName: name,
            ActiveKeyVersion: nextVersion,
            PublicKeySha256: fingerprint);
    }

    /// <summary>
    /// Read the public-key SHA-256 fingerprint of the institution's currently
    /// ACTIVE trust-directory row. Returns the highest-version ACTIVE row's
    /// <c>public_key_sha256</c>, or <c>null</c> if no ACTIVE row exists for
    /// the institution. Cheap reader used by the Adopt-mode drift guard in
    /// <c>GenerateOrAdoptCryptoKeyCommandHandler</c> so a tenant-supplied
    /// PEM pair cannot silently rotate away an existing trust row.
    /// </summary>
    public async Task<string?> GetActivePublicKeySha256Async(
        string institutionCode,
        CancellationToken cancellationToken)
    {
        var code = (institutionCode ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        return await _db.Keys
            .Where(k => k.InstitutionCode == code && k.Status == "ACTIVE")
            .OrderByDescending(k => k.KeyVersion)
            .Select(k => (string?)k.PublicKeySha256)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Explicit revocation, per the design spec's non-destructive-sync rule
    /// ("revocation is only ever explicit, never inferred from absence"):
    /// the trust store reported this institution's key with a non-ACTIVE
    /// status, so the locally-ACTIVE key stops being trusted. Retires the
    /// current ACTIVE key (local vocabulary: RETIRED) and creates NO new key
    /// row. Retired rows stay queryable so historical QRs remain auditable.
    /// Sets <c>revoked_at</c> and closes <c>valid_to</c> at the moment of
    /// revocation so the C4/C16 gate's "expired-or-revoked → reject" rule
    /// has the timestamps it needs.
    /// A no-op when we hold no ACTIVE key for the institution.
    /// </summary>
    public async Task RetireKeyAsync(
        string institutionCode,
        string reportedStatus,
        string actorId,
        CancellationToken cancellationToken)
    {
        var code = (institutionCode ?? string.Empty).Trim();
        if (!TrustRecordPatterns.InstitutionCode().IsMatch(code))
        {
            throw new InvalidTrustStoreRecordException(
                code, "institution code must be exactly six digits.");
        }

        var activeKeys = await _db.Keys
            .Where(k => k.InstitutionCode == code && k.Status == "ACTIVE")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeKeys.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var existing in activeKeys)
        {
            existing.Status = "RETIRED";
            existing.RevokedAt = now;
            existing.ValidTo = now;
            existing.ModifiedBy = actorId;
            existing.ModifiedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.LogAsync(new AuditEntry(
            Action: "institution.trust.key.revoked",
            ActorId: actorId,
            ResourceType: "institution",
            ResourceId: code,
            Metadata: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["reported_status"] = reportedStatus,
                ["retired_versions"] = activeKeys.Select(k => k.KeyVersion).ToList(),
            })),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Propagate a KeyCustody lifecycle transition into the trust directory.
    /// Called by <see cref="InstitutionTrustPublisher"/> when KeyCustody
    /// suspends, retires, or revokes a tenant's signing key. This keeps the
    /// trust store's status column and validity window in sync with
    /// KeyCustody's authoritative state so Verification (which reads only
    /// from InstitutionTrust) sees the same lifecycle.
    ///
    /// <para>
    /// State transitions:
    /// <list type="bullet">
    ///   <item><c>SUSPENDED</c> → status = SUSPENDED, revoked_at = now (fail-closed until reinstated)</item>
    ///   <item><c>RETIRED</c> → status = RETIRED, valid_to = now (keeps historical verify)</item>
    ///   <item><c>REVOKED</c> → status = REVOKED, revoked_at = now + valid_to = now (fail-closed, never verifies)</item>
    ///   <item><c>ACTIVE</c> → status = ACTIVE, valid_to = null, revoked_at = null (reinstatement)</item>
    /// </list>
    /// </para>
    ///
    /// A no-op when no ACTIVE row exists for the institution (the key may
    /// have already been superseded or the institution was never published).
    /// </para>
    /// Returns the number of rows updated (0 = no ACTIVE row found).
    /// </summary>
    public async Task<int> UpdateKeyStatusAsync(
        string institutionCode,
        string newStatus,
        string actorId,
        CancellationToken cancellationToken)
    {
        var code = (institutionCode ?? string.Empty).Trim();
        if (!TrustRecordPatterns.InstitutionCode().IsMatch(code))
        {
            throw new InvalidTrustStoreRecordException(
                code, "institution code must be exactly six digits.");
        }

        if (string.IsNullOrWhiteSpace(newStatus))
        {
            throw new ArgumentException("newStatus is required.", nameof(newStatus));
        }

        var normalized = newStatus.ToUpperInvariant();
        if (!IsSupportedStatus(normalized))
        {
            throw new ArgumentException(
                $"Unsupported trust-store status '{newStatus}'. " +
                $"Supported: ACTIVE, SUSPENDED, RETIRED, REVOKED.",
                nameof(newStatus));
        }

        var activeKeys = await _db.Keys
            .Where(k => k.InstitutionCode == code && k.Status == "ACTIVE")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeKeys.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var existing in activeKeys)
        {
            existing.Status = normalized;
            existing.ModifiedBy = actorId;
            existing.ModifiedAt = now;

            // NOTE: IsActive is a soft-delete flag for institution-level
            // removal (entire rows taken off the directory). It is NOT
            // touched here — status transitions flow through the Status
            // column + temporal fields, and the query handler uses
            // IgnoreQueryFilters() so IsActive does not affect reads.
            switch (normalized)
            {
                case "SUSPENDED":
                    existing.RevokedAt = now;
                    existing.ValidTo = now;
                    break;
                case "RETIRED":
                    existing.RevokedAt = null;
                    existing.ValidTo = now;
                    break;
                case "REVOKED":
                    existing.RevokedAt = now;
                    existing.ValidTo = now;
                    break;
                case "ACTIVE":
                    existing.RevokedAt = null;
                    existing.ValidTo = null;
                    break;
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.LogAsync(new AuditEntry(
            Action: "institution.trust.key.status_updated",
            ActorId: actorId,
            ResourceType: "institution",
            ResourceId: code,
            Metadata: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["status"] = normalized,
                ["updated_versions"] = activeKeys.Select(k => k.KeyVersion).ToList(),
            })),
            cancellationToken).ConfigureAwait(false);

        return activeKeys.Count;
    }

    /// <summary>
    /// The trust-store status vocabulary that Verification can map to
    /// verdicts. KeyCustody-internal states (GENERATING, PENDING, etc.)
    /// are handled within KeyCustody and never reach the trust store.
    /// </summary>
    private static bool IsSupportedStatus(string status) =>
        status is "ACTIVE" or "SUSPENDED" or "RETIRED" or "REVOKED";

    /// <summary>
    /// Defence-in-depth shape guard, applied on EVERY write path. The manual
    /// admin path already ran <c>UpsertInstitutionValidator</c> in the MediatR
    /// pipeline; the trust-sync path did not, and without this a malformed
    /// trust-store record would only be caught by the database CHECK
    /// constraints at SaveChanges — far too late.
    /// </summary>
    private static void Validate(string code, string type, string name, string pem)
    {
        if (!TrustRecordPatterns.InstitutionCode().IsMatch(code))
        {
            throw new InvalidTrustStoreRecordException(
                code, "institution code must be exactly six digits.");
        }

        if (!TrustRecordPatterns.InstituteType().IsMatch(type))
        {
            throw new InvalidTrustStoreRecordException(
                code, "institute type must be exactly two digits (Tag 26 sub 01).");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidTrustStoreRecordException(
                code, "institution name must be non-blank (NOT NULL on institution_keys).");
        }

        if (!TrustRecordPatterns.Pem().IsMatch(pem))
        {
            throw new InvalidTrustStoreRecordException(
                code, "public key must be a PEM block ('-----BEGIN PUBLIC KEY-----').");
        }
    }
}
