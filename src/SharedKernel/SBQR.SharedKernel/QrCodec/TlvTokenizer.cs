using System.Text;

namespace SBQR.SharedKernel.QrCodec;

/// <summary>
/// One parsed TLV field: 2-digit tag ID, raw value, and the field's starting
/// offset in the payload's UTF-8 byte representation (for FR-PARSE-02 error
/// reporting). The value is the exact original substring — never re-encoded,
/// normalized, or trimmed — so a parse → reconstruct → verify chain sees the
/// same bytes the signer saw (FR-PARSE-01: no re-serialization on the parse path).
/// </summary>
public sealed record TlvField(string TagId, string Value, int ByteOffset)
{
    /// <summary>Serializes this field as [ID][len][value] with the byte length of the value.</summary>
    public string ToTlv() => string.Concat(
        TagId,
        Utf8Length.ByteCount(Value).ToString("00", System.Globalization.CultureInfo.InvariantCulture),
        Value);
}

/// <summary>
/// Tokenizer for EMV-QRCPS-style TLV on a raw payload string. Walks
/// [2-digit ID][2-digit zero-padded decimal length][value] repeatedly over
/// the string's UTF-8 byte representation — the length prefix counts bytes,
/// not chars, which is what makes multi-byte (e.g. Bangla-script) values
/// locate their next-tag boundary correctly.
///
/// This type performs no semantic validation (no duplicate check, no
/// mandatory-tag check, no CRC): every later rule operates on the shared
/// <see cref="TlvField"/> sequence it produces.
/// </summary>
public static class TlvTokenizer
{
    /// <summary>
    /// Tokenizes <paramref name="payload"/> in one pass. Returns the fields
    /// parsed before any failure plus the structural errors; a non-empty
    /// error list means the payload is malformed and must not be processed
    /// further (fail-closed).
    /// </summary>
    public static (IReadOnlyList<TlvField> Fields, IReadOnlyList<QrStructuralError> Errors) Tokenize(
        string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var bytes = Encoding.UTF8.GetBytes(payload);
        var fields = new List<TlvField>();
        var errors = new List<QrStructuralError>();

        var offset = 0;
        while (offset < bytes.Length)
        {
            // Two digits of tag ID.
            if (offset + 2 > bytes.Length
                || !char.IsAsciiDigit((char)bytes[offset])
                || !char.IsAsciiDigit((char)bytes[offset + 1]))
            {
                errors.Add(new QrStructuralError(
                    QrStructuralReasonCode.MalformedTlv,
                    $"Tag ID at byte {offset} is not two decimal digits.",
                    ByteOffset: offset));
                break;
            }

            var tagId = $"{(char)bytes[offset]}{(char)bytes[offset + 1]}";
            var fieldStart = offset;
            offset += 2;

            // Two digits of decimal length (value length in bytes).
            if (offset + 2 > bytes.Length
                || !char.IsAsciiDigit((char)bytes[offset])
                || !char.IsAsciiDigit((char)bytes[offset + 1]))
            {
                errors.Add(new QrStructuralError(
                    QrStructuralReasonCode.MalformedTlv,
                    $"Length prefix for tag {tagId} at byte {offset} is not two decimal digits.",
                    ByteOffset: offset));
                break;
            }

            var declaredLength = (bytes[offset] - '0') * 10 + (bytes[offset + 1] - '0');
            offset += 2;

            var valueEnd = offset + declaredLength;
            if (valueEnd > bytes.Length)
            {
                errors.Add(new QrStructuralError(
                    QrStructuralReasonCode.TruncatedValue,
                    $"Tag {tagId} declares {declaredLength} bytes but only {bytes.Length - offset} remain.",
                    ByteOffset: fieldStart));
                break;
            }

            var value = Encoding.UTF8.GetString(bytes, offset, declaredLength);
            fields.Add(new TlvField(tagId, value, fieldStart));
            offset = valueEnd;
        }

        return (fields, errors);
    }
}
