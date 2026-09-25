using FluentValidation;
using MediatR;
using SBQR.Modules.InstitutionTrust.Application.Services;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.InstitutionTrust.Application.Commands;

/// <summary>
/// Admin seed path for the manual trust directory (Sep-17 scope): register
/// (or update) an institution and publish an SPKI public-key PEM as its new
/// ACTIVE key version, retiring the previous active key.
///
/// <para>
/// Per the design spec this endpoint is a PERMANENT fallback that lives
/// alongside the BB Trust-Sync pipeline — it is not replaced by it. It stays
/// the escape hatch for an institution BB has not yet published (or has
/// published wrongly) and for local/dev seeding when no trust store is
/// reachable.
/// </para>
///
/// <para>
/// The human-readable <see cref="InstitutionName"/> and the Annex A
/// Institution Type <see cref="InstituteType"/> are denormalised onto
/// <c>institution_keys</c> alongside the cryptographic publication so a
/// verifier resolving a QR gets the issuer's identity without a join onto
/// <c>public.tenants</c> (both columns are NOT NULL on the row).
/// </para>
/// </summary>
public sealed record UpsertInstitutionCommand(
    string InstitutionCode,
    string InstituteType,
    string InstitutionName,
    string PublicKeyPem) : IRequest<Result<UpsertedInstitution>>;

/// <summary>The result of an upsert: the institution identity + the key version that is now ACTIVE.</summary>
public sealed record UpsertedInstitution(
    string InstitutionCode,
    string InstituteType,
    string InstitutionName,
    int ActiveKeyVersion,
    string PublicKeySha256);

/// <summary>
/// FIRST line of defence for the manual admin path: turns a malformed
/// request into a clean <c>Result.Failure</c> via the MediatR
/// <c>ValidationBehavior</c>. It shares its rules with
/// <see cref="TrustRecordPatterns"/>, which <see cref="InstitutionUpsertService"/>
/// re-checks internally — the trust-sync path never runs through MediatR, so
/// that internal guard is the only protection it has.
/// </summary>
public sealed class UpsertInstitutionValidator : AbstractValidator<UpsertInstitutionCommand>
{
    public UpsertInstitutionValidator()
    {
        RuleFor(x => x.InstitutionCode)
            .Matches(TrustRecordPatterns.InstitutionCode())
            .WithMessage("Institution code must be exactly six digits.");
        RuleFor(x => x.InstituteType)
            .Matches(TrustRecordPatterns.InstituteType())
            .WithMessage("Institute type must be exactly two digits (Tag 26 sub 01 — the BB InstitutionId prefix).");
        RuleFor(x => x.InstitutionName)
            .NotEmpty()
            .MaximumLength(200)
            .WithMessage("Institution name is required (NOT NULL on institution_keys) and must be ≤ 200 characters.");
        RuleFor(x => x.PublicKeyPem)
            .Matches(TrustRecordPatterns.Pem())
            .WithMessage("Public key must be a PEM block (SPKI Ed25519 '-----BEGIN PUBLIC KEY-----').");
    }
}

public sealed class UpsertInstitutionHandler
    : IRequestHandler<UpsertInstitutionCommand, Result<UpsertedInstitution>>
{
    private readonly InstitutionUpsertService _upsertService;

    public UpsertInstitutionHandler(InstitutionUpsertService upsertService)
    {
        _upsertService = upsertService ?? throw new ArgumentNullException(nameof(upsertService));
    }

    public async Task<Result<UpsertedInstitution>> Handle(
        UpsertInstitutionCommand request,
        CancellationToken cancellationToken)
    {
        var result = await _upsertService.UpsertAsync(
            request.InstitutionCode,
            request.InstituteType,
            request.InstitutionName,
            request.PublicKeyPem,
            actorId: "system",
            cancellationToken).ConfigureAwait(false);

        return result;
    }
}
