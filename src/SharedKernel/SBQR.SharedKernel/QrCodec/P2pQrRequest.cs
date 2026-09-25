namespace SBQR.SharedKernel.QrCodec;

/// <summary>
/// Wire constants from the BanglaQR P2P specification tables this codec
/// implements. Centralized so tag numbers and fixed values have exactly one
/// spelling in the module.
/// </summary>
public static class BqrConstants
{
    /// <summary>Globally Unique Identifier identifying the NPSB network (Tables 3B/5A/5B sub-tag "00").</summary>
    public const string NpsbNetworkGuid = "bd.org.bb.npsb";

    /// <summary>Merchant Category Code fixed value marking a P2P QR (Table 3A, tag "52").</summary>
    public const string P2pMerchantCategoryCode = "4829";

    /// <summary>Payload Format Indicator fixed value (Table 3A, tag "00").</summary>
    public const string PayloadFormatIndicator = "01";

    /// <summary>Point of Initiation Method value for static QRs (Table 3A, tag "01").</summary>
    public const string StaticInitiation = "11";

    /// <summary>Point of Initiation Method value for dynamic QRs (Table 3A, tag "01").</summary>
    public const string DynamicInitiation = "12";
}

/// <summary>
/// Strongly-typed builder input for a P2P QR payload (epic-2 Story 5). Field
/// presence mirrors Table 3A/3B/4A: mandatory constructor parameters are the
/// M rows; optional parameters are the O rows and are omitted entirely when
/// not supplied — never emitted empty.
/// </summary>
/// <param name="IsDynamic">Tag 01: <c>true</c> emits "12" (dynamic), <c>false</c> emits "11" (static). Always emitted.</param>
/// <param name="InstitutionType">Tag 26 sub 01 — two digits, 00–05 (banks … WLAMA).</param>
/// <param name="InstitutionId">Tag 26 sub 02 — exactly four digits (Annex A).</param>
/// <param name="RecipientPan">Tag 26 sub 03 — account/wallet number, up to 19 bytes.</param>
/// <param name="RecipientName">Tag 59 — up to 25 UTF-8 bytes.</param>
/// <param name="RecipientCity">Tag 60 — up to 15 UTF-8 bytes.</param>
/// <param name="CountryCode">Tag 58 — ISO 3166 two-letter, default "BD".</param>
/// <param name="Currency">Tag 53 — ISO 4217 three-digit numeric, default "050" (BDT).</param>
/// <param name="TransactionAmount">Tag 54 (optional) — up to 13 bytes, digits with optional single decimal point.</param>
/// <param name="PostalCode">Tag 61 (optional) — up to 10 bytes.</param>
/// <param name="CustomerLabel">Tag 62 sub 06 (optional) — up to 25 bytes, e.g. "FT".</param>
/// <param name="PurposeOfTransaction">Tag 62 sub 08 (optional) — up to 25 bytes; "***" prompts the payer.</param>
public sealed record P2pQrRequest(
    bool IsDynamic,
    string InstitutionType,
    string InstitutionId,
    string RecipientPan,
    string RecipientName,
    string RecipientCity,
    string CountryCode = "BD",
    string Currency = "050",
    string? TransactionAmount = null,
    string? PostalCode = null,
    string? CustomerLabel = null,
    string? PurposeOfTransaction = null);

/// <summary>
/// Builder output: the canonical business-tag partial payload (tags
/// 00…62 — no signature tags, no CRC) plus the exact bytes the caller must
/// have signed. Keeps the "signature not yet available" step separately
/// testable from finalization.
/// </summary>
public sealed record UnsignedQrPayload(string PartialPayload, byte[] SignaturePayloadBytes);
