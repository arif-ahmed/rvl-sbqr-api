namespace SBQR.SharedKernel.QrCodec;

/// <summary>
/// Structural failure codes for the QR payload codec — the module's published
/// language for "this payload is not well-formed". Downstream modules
/// (QrGeneration, Verification) pattern-match on <see cref="StableCode"/> strings,
/// which are frozen: renaming one breaks two epics simultaneously.
///
/// Boundary: this catalog covers <b>structural</b> failures only (malformed
/// TLV, CRC, length rules, shape checks on Tag 26). Business/trust verdicts
/// such as <c>SIGNATURE_INVALID</c>, <c>KEY_REVOKED</c>, or
/// <c>KEY_NOT_FOUND</c> belong to Epic 8 (Verification) and must never be
/// added here.
/// </summary>
public enum QrStructuralReasonCode
{
    /// <summary>A tag ID or length prefix is not numeric, or Tag 63's length is not "04".</summary>
    MalformedTlv = 1,

    /// <summary>A declared value length exceeds the remaining input bytes.</summary>
    TruncatedValue = 2,

    /// <summary>The same top-level tag ID appears more than once.</summary>
    DuplicateTag = 3,

    /// <summary>A tag the spec marks Mandatory (M) is absent. Message names the tag.</summary>
    MandatoryTagMissing = 4,

    /// <summary>A field value (or the whole payload) exceeds its documented byte limit.</summary>
    QrLengthOverflow = 5,

    /// <summary>Tag 26 sub-tag 00 (Globally Unique Identifier) is not "bd.org.bb.npsb".</summary>
    Tag26GuidMismatch = 6,

    /// <summary>Tag 26 sub-tag 01 (Institution Type) is not a 2-digit value in 00–05.</summary>
    Tag26InvalidType = 7,

    /// <summary>Tag 26 sub-tag 02 (Institution ID) is not exactly 4 digits.</summary>
    Tag26InvalidId = 8,

    /// <summary>A mandatory Tag 26 sub-tag (00/01/02/03) is absent. Message names the sub-tag.</summary>
    Tag26SubTagMissing = 9,

    /// <summary>Tag 63's value does not match the CRC-16/CCITT-FALSE over the payload.</summary>
    CrcMismatch = 10,

    /// <summary>The signature supplied to the finalizer is not exactly 88 Base64 characters.</summary>
    InvalidSignatureLength = 11,

    /// <summary>The signature contains Base64URL characters or is otherwise not standard-alphabet Base64.</summary>
    InvalidBase64Alphabet = 12,
}

/// <summary>
/// A single structural failure: stable code + human message + UTF-8 byte
/// offset into the raw payload when the failure is localizable
/// (FR-PARSE-02). Offset is <c>null</c> when no single byte position applies
/// (e.g. <see cref="QrStructuralReasonCode.MandatoryTagMissing"/>).
/// </summary>
public sealed record QrStructuralError(
    QrStructuralReasonCode Code,
    string Message,
    int? ByteOffset = null);

/// <summary>Stable wire-format names for <see cref="QrStructuralReasonCode"/>.</summary>
public static class QrStructuralReasonCodes
{
    /// <summary>Returns the frozen uppercase snake-case code for pattern matching by consumers.</summary>
    public static string StableCode(this QrStructuralReasonCode code) => code switch
    {
        QrStructuralReasonCode.MalformedTlv => "MALFORMED_TLV",
        QrStructuralReasonCode.TruncatedValue => "TRUNCATED_VALUE",
        QrStructuralReasonCode.DuplicateTag => "DUPLICATE_TAG",
        QrStructuralReasonCode.MandatoryTagMissing => "MANDATORY_TAG_MISSING",
        QrStructuralReasonCode.QrLengthOverflow => "QR_LENGTH_OVERFLOW",
        QrStructuralReasonCode.Tag26GuidMismatch => "TAG26_GUID_MISMATCH",
        QrStructuralReasonCode.Tag26InvalidType => "TAG26_INVALID_TYPE",
        QrStructuralReasonCode.Tag26InvalidId => "TAG26_INVALID_ID",
        QrStructuralReasonCode.Tag26SubTagMissing => "TAG26_SUBTAG_MISSING",
        QrStructuralReasonCode.CrcMismatch => "CRC_MISMATCH",
        QrStructuralReasonCode.InvalidSignatureLength => "INVALID_SIGNATURE_LENGTH",
        QrStructuralReasonCode.InvalidBase64Alphabet => "INVALID_BASE64_ALPHABET",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unhandled reason code; update StableCode and the catalog snapshot test together."),
    };
}
