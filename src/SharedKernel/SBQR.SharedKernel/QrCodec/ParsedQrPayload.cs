namespace SBQR.SharedKernel.QrCodec;

/// <summary>P2P classification outcome of a structurally valid parse (story 8).</summary>
public enum QrClassification
{
    /// <summary>Tag 52 = "4829" — the payload is a P2P BanglaQR.</summary>
    P2P = 0,

    /// <summary>Any other Tag 52 value — a valid outcome, not an error (FR-PARSE-07).</summary>
    NonP2P = 1,
}

/// <summary>
/// The structurally validated, field-decoded view of a raw QR payload
/// (stories 4/8). Preserves every original TLV field — including
/// unknown/RFU tags 65–79 and 82–99, byte-for-byte in original order —
/// because the CRC covers the entire string and silent drops would
/// desynchronize round-trip checks. Typed accessors expose only what the
/// P2P flow needs; Tag 62/64 stay opaque templates (BB-CLARIFY #B6).
/// </summary>
public sealed class ParsedQrPayload
{
    private readonly Dictionary<string, TlvField> _byTag;

    internal ParsedQrPayload(
        IReadOnlyList<TlvField> fields,
        IReadOnlyDictionary<string, TlvField> tag26SubTags,
        string classification)
    {
        Fields = fields;
        _byTag = fields.GroupBy(f => f.TagId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Tag26SubTags = tag26SubTags;
        ClassificationRaw = classification;
    }

    /// <summary>All parsed top-level fields in original byte order, RFU tags included.</summary>
    public IReadOnlyList<TlvField> Fields { get; }

    /// <summary>Tag 26's parsed sub-tags (first occurrence wins). Keyed by sub-tag id ("00"–"03").</summary>
    public IReadOnlyDictionary<string, TlvField> Tag26SubTags { get; }

    /// <summary>Raw Tag 52 value; "4829" means P2P.</summary>
    public string ClassificationRaw { get; }

    /// <summary><see cref="QrClassification.P2P"/> when Tag 52 = "4829", else <see cref="QrClassification.NonP2P"/>.</summary>
    public QrClassification Classification =>
        ClassificationRaw == BqrConstants.P2pMerchantCategoryCode
            ? QrClassification.P2P
            : QrClassification.NonP2P;

    /// <summary>Tag 59 — Recipient Name (exact raw value).</summary>
    public string? RecipientName => Field("59")?.Value;

    /// <summary>Tag 60 — Recipient City.</summary>
    public string? RecipientCity => Field("60")?.Value;

    /// <summary>Tag 58 — Country Code.</summary>
    public string? CountryCode => Field("58")?.Value;

    /// <summary>Tag 53 — Transaction Currency.</summary>
    public string? Currency => Field("53")?.Value;

    /// <summary>Tag 54 — Transaction Amount (optional).</summary>
    public string? Amount => Field("54")?.Value;

    /// <summary>Tag 01 — Point of Initiation ("11" static, "12" dynamic), when present.</summary>
    public string? PointOfInitiation => Field("01")?.Value;

    /// <summary>Tag 26 sub 00 — network GUID (must be bd.org.bb.npsb on a valid parse).</summary>
    public string? NetworkGuid => SubTag("00")?.Value;

    /// <summary>Tag 26 sub 01 — Recipient's Institution Type (e.g. "03").</summary>
    public string? InstitutionType => SubTag("01")?.Value;

    /// <summary>Tag 26 sub 02 — Recipient's Institution ID (4 digits).</summary>
    public string? InstitutionId => SubTag("02")?.Value;

    /// <summary>Tag 26 sub 03 — Recipient PAN (account/wallet number).</summary>
    public string? RecipientPan => SubTag("03")?.Value;

    /// <summary>
    /// The trust-store lookup key per Annex B step 2: Institution Type ‖
    /// Institution ID (sub 01 + sub 02), e.g. "03" + "1008" → "031008" —
    /// the same six-digit shape as Tenancy's <c>institution_code</c>.
    /// Constructed via <see cref="SharedKernel.QrCodec.InstitutionId.FromComponents"/>
    /// so the spec's Institution_ID formula lives in one place.
    /// </summary>
    public InstitutionId? ResolvedInstitutionId
        => InstitutionType is { } type && SubTag("02") is { } id
            ? SBQR.SharedKernel.QrCodec.InstitutionId.FromComponents(type, id.Value)
            : null;

    /// <summary>
    /// The joined 88-char Base64 signature (Tag 80 sub 01 + Tag 81 sub 01),
    /// or <c>null</c> when either signature tag is absent. Presence of the
    /// tags is structural; whether the signature verifies is Verification's
    /// verdict, not this module's.
    /// </summary>
    public string? SignatureBase64
    {
        get
        {
            var part1 = SubFieldValue("80", "01");
            var part2 = SubFieldValue("81", "01");
            return part1 is null || part2 is null ? null : string.Concat(part1, part2);
        }
    }

    /// <summary>
    /// The reconstructed signature payload (Tag 59 ‖ Tag 26 sub 03, UTF-8)
    /// via the single shared <see cref="SignaturePayload.Reconstruct"/> —
    /// the same function QrGeneration signs with. Throws if the mandatory
    /// fields are somehow absent (cannot happen on a successful parse).
    /// </summary>
    public byte[] GetSignaturePayloadBytes() =>
        SignaturePayload.Reconstruct(RecipientName!, RecipientPan!);

    /// <summary>Gets a top-level field's raw value, or <c>null</c> when the tag is absent.</summary>
    public string? FieldValue(string tag) => Field(tag)?.Value;

    private TlvField? Field(string tag) =>
        _byTag.TryGetValue(tag, out var field) ? field : null;

    private TlvField? SubTag(string subTagId) =>
        Tag26SubTags.TryGetValue(subTagId, out var field) ? field : null;

    private string? SubFieldValue(string tagId, string subTagId)
    {
        var tag = Field(tagId);
        if (tag is null)
        {
            return null;
        }

        var (subs, errors) = TlvTokenizer.Tokenize(tag.Value);
        return errors.Count > 0
            ? null
            : subs.FirstOrDefault(s => s.TagId == subTagId)?.Value;
    }
}
