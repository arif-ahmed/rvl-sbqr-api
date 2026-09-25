using System.Text;

namespace SBQR.SharedKernel.QrCodec;

/// <summary>Parse options. The total-payload byte cap is configurable because the exact
/// value is an open clarification ([BB-CLARIFY #B11]) — a setting, not a hardcoded constant.</summary>
public sealed record QrParseOptions(int MaxPayloadBytes = 2048);

/// <summary>Result envelope for the parser: a fully validated payload or the structural errors.</summary>
public sealed class QrParseResult
{
    private readonly ParsedQrPayload? _payload;

    private QrParseResult(ParsedQrPayload? payload, IReadOnlyList<QrStructuralError> errors)
    {
        _payload = payload;
        Errors = errors;
    }

    public bool IsSuccess => _payload is not null;

    public ParsedQrPayload Payload => _payload is not null
        ? _payload
        : throw new InvalidOperationException("Cannot read Payload from a failed QrParseResult.");

    public IReadOnlyList<QrStructuralError> Errors { get; }

    public static QrParseResult Ok(ParsedQrPayload payload) => new(payload, Array.Empty<QrStructuralError>());

    public static QrParseResult Failure(IReadOnlyList<QrStructuralError> errors) => new(null, errors);
}

/// <summary>
/// Structural parser and validator (stories 1/3/4/8). Pipeline order is
/// deliberate and fail-closed: total-length cap → tokenize → CRC validation
/// FIRST (before any deeper field work — no wasted effort on corrupted
/// payloads, and no partial result is ever returned as if valid) →
/// duplicate tags → mandatory presence (Table 3A M rows) → Tag 26 sub-tag
/// shape (Table 3B) → per-field byte caps → P2P classification.
///
/// NON_P2P is a valid outcome, not an error: any other Tag 52 value
/// classifies the payload <see cref="QrClassification.NonP2P"/> and takes
/// no further P2P-specific action (FR-PARSE-07).
/// </summary>
public static class QrPayloadParser
{
    private static readonly string[] MandatoryTags = ["00", "26", "52", "53", "58", "59", "60", "63"];

    /// <summary>Parses and structurally validates a raw QR payload string.</summary>
    public static QrParseResult Parse(string payload, QrParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        options ??= new QrParseOptions();

        if (Utf8Length.ByteCount(payload) > options.MaxPayloadBytes)
        {
            return QrParseResult.Failure(
            [
                new QrStructuralError(
                    QrStructuralReasonCode.QrLengthOverflow,
                    $"Payload is {Utf8Length.ByteCount(payload)} UTF-8 bytes; the cap is {options.MaxPayloadBytes}.",
                    ByteOffset: 0),
            ]);
        }

        var (fields, errors) = TlvTokenizer.Tokenize(payload);
        if (errors.Count > 0)
        {
            return QrParseResult.Failure(errors);
        }

        // CRC first (story 8). The CRC covers everything up to and including
        // the "6304" prefix — i.e. all bytes before the 4-hex value.
        var crcField = fields.FirstOrDefault(f => f.TagId == "63");
        if (crcField is not null)
        {
            if (crcField.Value.Length != 4)
            {
                return QrParseResult.Failure(
                [
                    new QrStructuralError(
                        QrStructuralReasonCode.MalformedTlv,
                        "Tag 63 (CRC) value must be exactly four hex characters.",
                        ByteOffset: crcField.ByteOffset),
                ]);
            }

            var bytes = Encoding.UTF8.GetBytes(payload);
            var crcInputEnd = crcField.ByteOffset + 4; // includes tag id + length prefix "6304"
            var expected = Crc16CcittFalse.Compute(bytes.AsSpan(0, crcInputEnd));
            if (!string.Equals(expected, crcField.Value, StringComparison.OrdinalIgnoreCase))
            {
                return QrParseResult.Failure(
                [
                    new QrStructuralError(
                        QrStructuralReasonCode.CrcMismatch,
                        $"CRC mismatch: payload carries {crcField.Value}, computed {expected}.",
                        ByteOffset: crcField.ByteOffset),
                ]);
            }
        }

        // Duplicate top-level tags (FR-PARSE-03).
        var duplicates = fields
            .GroupBy(f => f.TagId, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new QrStructuralError(
                QrStructuralReasonCode.DuplicateTag,
                $"Tag {g.Key} appears {g.Count()} times.",
                ByteOffset: g.Skip(1).First().ByteOffset))
            .ToList();
        if (duplicates.Count > 0)
        {
            return QrParseResult.Failure(duplicates);
        }

        // Mandatory presence (Table 3A M rows + CRC). A missing tag names the tag.
        var missing = MandatoryTags
            .Where(tag => fields.All(f => f.TagId != tag))
            .Select(tag => new QrStructuralError(
                QrStructuralReasonCode.MandatoryTagMissing,
                $"Mandatory tag {tag} is absent."))
            .ToList();
        if (missing.Count > 0)
        {
            return QrParseResult.Failure(missing);
        }

        // Tag 26 sub-tag shape (Table 3B).
        var tag26 = fields.First(f => f.TagId == "26");
        var (subs, subErrors) = TlvTokenizer.Tokenize(tag26.Value);
        if (subErrors.Count > 0)
        {
            return QrParseResult.Failure(subErrors);
        }

        var tag26Errors = new List<QrStructuralError>();
        var subIndex = subs
            .GroupBy(s => s.TagId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (!subIndex.TryGetValue("00", out var guid) || guid.Value != BqrConstants.NpsbNetworkGuid)
        {
            tag26Errors.Add(new QrStructuralError(
                QrStructuralReasonCode.Tag26GuidMismatch,
                $"Tag 26 sub 00 must be '{BqrConstants.NpsbNetworkGuid}' (found '{guid?.Value ?? "<absent>"}').",
                ByteOffset: tag26.ByteOffset));
        }

        if (!subIndex.TryGetValue("01", out var type)
            || type.Value.Length != 2
            || !type.Value.All(char.IsAsciiDigit)
            || int.Parse(type.Value, System.Globalization.CultureInfo.InvariantCulture) > P2pQrBuilder.MaxInstitutionType)
        {
            tag26Errors.Add(new QrStructuralError(
                QrStructuralReasonCode.Tag26InvalidType,
                "Tag 26 sub 01 (Institution Type) must be two digits 00–05.",
                ByteOffset: tag26.ByteOffset));
        }

        if (!subIndex.TryGetValue("02", out var institutionId)
            || institutionId.Value.Length != 4
            || !institutionId.Value.All(char.IsAsciiDigit))
        {
            tag26Errors.Add(new QrStructuralError(
                QrStructuralReasonCode.Tag26InvalidId,
                "Tag 26 sub 02 (Institution ID) must be exactly four digits.",
                ByteOffset: tag26.ByteOffset));
        }

        if (!subIndex.TryGetValue("03", out var pan))
        {
            tag26Errors.Add(new QrStructuralError(
                QrStructuralReasonCode.Tag26SubTagMissing,
                "Mandatory Tag 26 sub 03 (Recipient PAN) is absent.",
                ByteOffset: tag26.ByteOffset));
        }
        else if (Utf8Length.ByteCount(pan.Value) > P2pQrBuilder.MaxRecipientPanBytes)
        {
            tag26Errors.Add(new QrStructuralError(
                QrStructuralReasonCode.QrLengthOverflow,
                $"Tag 26 sub 03 exceeds {P2pQrBuilder.MaxRecipientPanBytes} UTF-8 bytes.",
                ByteOffset: pan.ByteOffset));
        }

        // Per-field byte caps (story 3's parse-side rule).
        AddCapError(fields, "59", P2pQrBuilder.MaxRecipientNameBytes, "Recipient Name (Tag 59)", tag26Errors);
        AddCapError(fields, "60", P2pQrBuilder.MaxRecipientCityBytes, "Recipient City (Tag 60)", tag26Errors);
        AddCapError(fields, "61", P2pQrBuilder.MaxPostalCodeBytes, "Postal Code (Tag 61)", tag26Errors);
        AddCapError(fields, "54", P2pQrBuilder.MaxAmountBytes, "Transaction Amount (Tag 54)", tag26Errors);

        if (tag26Errors.Count > 0)
        {
            return QrParseResult.Failure(tag26Errors);
        }

        var classification = fields.First(f => f.TagId == "52").Value;
        return QrParseResult.Ok(new ParsedQrPayload(fields, subIndex, classification));
    }

    private static void AddCapError(
        IReadOnlyList<TlvField> fields,
        string tag,
        int maxBytes,
        string fieldName,
        List<QrStructuralError> errors)
    {
        var field = fields.FirstOrDefault(f => f.TagId == tag);
        if (field is not null && Utf8Length.ByteCount(field.Value) > maxBytes)
        {
            errors.Add(new QrStructuralError(
                QrStructuralReasonCode.QrLengthOverflow,
                $"{fieldName} exceeds {maxBytes} UTF-8 bytes.",
                ByteOffset: field.ByteOffset));
        }
    }
}
