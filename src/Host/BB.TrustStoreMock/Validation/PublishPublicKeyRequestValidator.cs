// src/BB.TrustStoreMock/Validation/PublishPublicKeyRequestValidator.cs
using FluentValidation;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;

namespace BB.TrustStoreMock.Validation;

/// <summary>
/// Body rules for the spec-shaped public-key upload (A6 house style — the
/// client is untrusted even against a mock, and malformed input must get a
/// 400 with field errors, never a 500). The institution id itself travels
/// in the route and is validated on the controller.
///
/// Rules mirror the spec and the module-side UpsertInstitutionValidator:
/// Institution_ID is the 6-digit concat of Tag 26 sub 01 (institution
/// type) + sub 02 (institution id, Annex A); the key is an Ed25519 SPKI
/// public-key PEM — the exact format Annex C generates with
/// <c>openssl pkey -pubout</c>.
/// </summary>
public sealed class PublishPublicKeyRequestValidator : AbstractValidator<PublishPublicKeyRequest>
{
    public PublishPublicKeyRequestValidator()
    {
        RuleFor(x => x.InstitutionName)
            .NotEmpty().WithMessage("institutionName is required.")
            .MaximumLength(200).WithMessage("institutionName must be at most 200 characters.");

        RuleFor(x => x.InstituteType)
            .Matches("^[0-9]{2}$")
            .When(x => !string.IsNullOrWhiteSpace(x.InstituteType))
            .WithMessage("instituteType, when supplied, must be exactly two digits (Tag 26 sub-tag 01).");

        RuleFor(x => x.PublicKeyPem)
            .NotEmpty().WithMessage("publicKeyPem is required.")
            .Must(BeAnEd25519PublicKeyPem)
            .WithMessage(
                "publicKeyPem must be an Ed25519 public key in SPKI PEM form " +
                "('-----BEGIN PUBLIC KEY-----'), as produced by: " +
                "openssl pkey -in <id>-private.pem -pubout -out <id>-public.pem (spec Annex C).");

        RuleFor(x => x.ValidTo)
            .Must((request, validTo) => validTo is null || request.ValidFrom is null || validTo > request.ValidFrom)
            .When(x => x.ValidTo is not null)
            .WithMessage("validTo must be after validFrom.");
    }

    /// <summary>
    /// Strict algorithm gate: parse the PEM and require an Ed25519 PUBLIC
    /// key parameter block. Rejects private keys, RSA/EC keys, certificates
    /// and malformed blobs — BouncyCastle is already the platform's crypto
    /// package (C2: approved algorithms only, spec: Ed25519).
    /// </summary>
    private static bool BeAnEd25519PublicKeyPem(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return false;
        }

        try
        {
            using var reader = new PemReader(new StringReader(pem));
            return reader.ReadObject() is Ed25519PublicKeyParameters;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
