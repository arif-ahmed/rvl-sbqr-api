using System.Text.RegularExpressions;

namespace SBQR.SharedKernel.QrCodec;

/// <summary>
/// Explicit value object for the Bangladesh Bank institution identifier
/// per Annex B step 2 of the P2P specification:
///
///   Institution_ID = Tag 26 Sub-tag 01 (Institution Type, 2 digits)
///                   + Tag 26 Sub-tag 02 (Institution ID, 4 digits)
///
/// e.g. "03" (Bank) + "1008" (institution code) → "031008"
///
/// This type exists so the verification and generation paths share one
/// spec-compliant construction path instead of independently slicing
/// 2-digit and 4-digit substrings — a mismatch here is a silent interop
/// failure (the wrong key is looked up, producing a spurious
/// KEY_NOT_FOUND). The BB trust store indexes by this exact 6-digit
/// value.
/// </summary>
public sealed record InstitutionId
{
    /// <summary>Tag 26 Sub-tag 01 — 2-digit Annex A Institution Type (e.g. "03" for banks).</summary>
    public string InstitutionType { get; }

    /// <summary>Tag 26 Sub-tag 02 — 4-digit Institution ID within the type.</summary>
    public string InstitutionCode { get; }

    /// <summary>The full 6-digit Institution ID used as the trust-store lookup key (spec Annex B).</summary>
    public string Value { get; }

    private InstitutionId(string institutionType, string institutionCode, string value)
    {
        InstitutionType = institutionType;
        InstitutionCode = institutionCode;
        Value = value;
    }

    /// <summary>
    /// Construct from the individual Tag 26 Sub-tags.
    /// Per Annex B: Institution Type is 2 digits (00–05), Institution ID is 4 digits.
    /// </summary>
    public static InstitutionId FromComponents(string institutionType, string institutionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionCode);

        if (institutionType.Length != 2 || !institutionType.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "Tag 26 Sub-tag 01 (Institution Type) must be exactly two digits.",
                nameof(institutionType));
        }

        if (institutionCode.Length != 4 || !institutionCode.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "Tag 26 Sub-tag 02 (Institution ID) must be exactly four digits.",
                nameof(institutionCode));
        }

        return new InstitutionId(institutionType, institutionCode, string.Concat(institutionType, institutionCode));
    }

    /// <summary>
    /// Construct from the 6-digit Institution ID string as it appears in the
    /// trust store. This is the form extracted from a parsed QR payload
    /// (Tag 26 Sub-tag 01 + Sub-tag 02 concatenated).
    /// </summary>
    public static InstitutionId FromCode(string sixDigitCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sixDigitCode);

        if (sixDigitCode.Length != 6 || !sixDigitCode.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "Institution ID must be exactly six digits (Tag 26 Sub-tag 01 || Sub-tag 02).",
                nameof(sixDigitCode));
        }

        return new InstitutionId(
            institutionType: sixDigitCode[..2],
            institutionCode: sixDigitCode[2..],
            value: sixDigitCode);
    }

    public override string ToString() => Value;
}
