// src/BB.TrustStoreMock/Controllers/TrustStoreController.cs
using System.Text.RegularExpressions;
using BB.TrustStoreMock.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BB.TrustStoreMock.Controllers;

/// <summary>
/// The "BB-shaped" surface — what HttpTrustStoreClient calls, plus the
/// public-key upload the spec describes. Shaped from
/// docs/bb-banglaqr-p2p-specification.md Annex B/C's own language, not from
/// a real published BB contract (none exists):
///
/// <list type="bullet">
///   <item>Annex B step 2: "retrieve the public key file using the derived
///   Institution_ID" → GET .../public-key and the directory feed.</item>
///   <item>Annex B/C: "Bank/MFS/PSP will share their public key with
///   Bangladesh Bank", one key file per Institution_ID
///   ({id}-public.pem) → PUT .../public-key. Idempotent by design: same key
///   material = no change (200), new key material = rotate (201).</item>
/// </list>
/// </summary>
[ApiController]
[Route("trust-store")]
public sealed partial class TrustStoreController : ControllerBase
{
    private static readonly Regex InstitutionIdPattern = RegexUtil.InstitutionId();

    private readonly TrustStoreRepository _store;
    private readonly IValidator<PublishPublicKeyRequest> _validator;

    public TrustStoreController(
        TrustStoreRepository store,
        IValidator<PublishPublicKeyRequest> validator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <summary>
    /// Full list of institutions, each reported with its MOST RECENT key
    /// whatever that key's status. A revoked institution therefore appears
    /// here with <c>activeKey.status = "REVOKED"</c> rather than dropping out
    /// of the feed: the sync client treats absence as "no change", so a
    /// disappearing institution could never propagate a revocation. Only an
    /// institution with no key at all is omitted (nothing to report).
    ///
    /// The `since` parameter is accepted and ignored — reserved for a future
    /// incremental-sync contract, not implemented by the client today.
    /// </summary>
    [HttpGet("institutions")]
    public async Task<ActionResult<IReadOnlyList<TrustStoreInstitutionDto>>> GetAll(
        [FromQuery] DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        var dtos = (await _store.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r.MostRecentKey is not null)
            .Select(r => ToDto(r, r.MostRecentKey!))
            .ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Resolve the currently trusted key for one institution (spec Annex B
    /// step 2). Deliberately still 404s when there is no ACTIVE key —
    /// "resolve me the key to trust" has no answer for a revoked institution.
    /// </summary>
    [HttpGet("institutions/{institutionId}/public-key")]
    public async Task<ActionResult<TrustStoreInstitutionDto>> GetOne(
        string institutionId,
        CancellationToken cancellationToken)
    {
        var record = await _store.GetAsync(institutionId, cancellationToken).ConfigureAwait(false);
        if (record?.ActiveKey is null)
        {
            return NotFound();
        }

        return Ok(ToDto(record, record.ActiveKey));
    }

    /// <summary>
    /// **Upload the public key for an institution** (spec Annex B/C: the
    /// institution shares its public key with the BB trust store — the
    /// "{Institution_ID}-public.pem" model). 201 when a new key version was
    /// minted, 200 when the upload was an idempotent no-op (same key
    /// material; identity metadata still refreshes). Uploading different key
    /// material retires the previous version and bumps <c>keyVersion</c>.
    /// </summary>
    [HttpPut("institutions/{institutionId}/public-key")]
    public async Task<ActionResult<TrustStoreInstitutionDto>> PublishPublicKey(
        string institutionId,
        [FromBody] PublishPublicKeyRequest request,
        CancellationToken cancellationToken)
    {
        // Route-parameter rules the body validator cannot see: the Annex B
        // Institution_ID is exactly six digits, and a supplied instituteType
        // must agree with its prefix (Tag 26 sub-tag 01). A6/C13: 400 with
        // field detail, never a 500 from a substring slice on a short id.
        if (!InstitutionIdPattern.IsMatch(institutionId))
        {
            ModelState.AddModelError(
                "institutionId",
                "institutionId must be exactly six digits: the Annex B Institution_ID " +
                "(Tag 26 sub-tag 01 institution type + sub-tag 02 institution id), e.g. 031008.");
            return ValidationProblem();
        }

        if (request is null)
        {
            ModelState.AddModelError(string.Empty, "A JSON request body is required.");
            return ValidationProblem();
        }

        if (!string.IsNullOrWhiteSpace(request.InstituteType) &&
            request.InstituteType != institutionId[..2])
        {
            ModelState.AddModelError(
                "instituteType",
                "instituteType must match the institutionId prefix (Tag 26 sub-tag 01).");
            return ValidationProblem();
        }

        var validation = await _validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem();
        }

        var result = await _store.UpsertAsync(
            institutionId,
            request.InstitutionName,
            request.PublicKeyPem,
            request.InstituteType,
            request.ValidFrom,
            request.ValidTo,
            cancellationToken).ConfigureAwait(false);

        return result.NewKeyVersion
            ? StatusCode(StatusCodes.Status201Created, ToDto(result.Snapshot))
            : Ok(ToDto(result.Snapshot));
    }

    /// <summary>Upload response view: the institution with its ACTIVE key, or the most recent key after a revoke-style edge.</summary>
    private static TrustStoreInstitutionDto ToDto(InstitutionSnapshot record) =>
        ToDto(record, record.ActiveKey ?? record.MostRecentKey ?? throw new InvalidOperationException(
            $"Institution {record.InstitutionId} has no keys at all after an upload."));

    private static TrustStoreInstitutionDto ToDto(
        InstitutionSnapshot record,
        KeySnapshot key) =>
        new(
            record.InstitutionId,
            record.InstituteType,
            record.InstitutionName,
            record.Status,
            new TrustStoreKeyDto(
                key.KeyVersion,
                key.PublicKeyPem,
                key.Sha256,
                key.Status,
                key.ValidFrom,
                key.ValidTo));

    internal static partial class RegexUtil
    {
        [GeneratedRegex("^[0-9]{6}$")]
        public static partial Regex InstitutionId();
    }
}
