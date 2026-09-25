using System.Text.RegularExpressions;

namespace SBQR.SharedKernel.QrCodec;

/// <summary>
/// Builds the canonical, spec-compliant P2P partial payload from a validated
/// <see cref="P2pQrRequest"/> (epic-2 Story 5). Output tag order is always
/// ascending regardless of input; Tags 80/81 (signature) and 63 (CRC) are
/// deliberately excluded — they belong to <see cref="QrPayloadFinalizer"/>,
/// which keeps the CRC-computed-last rule structurally enforceable.
/// </summary>
public static partial class P2pQrBuilder
{
    /// <summary>Institution Type must be two digits with a documented value (Table 3B sub 01).</summary>
    public const int MaxInstitutionType = 5;

    /// <summary>Recipient PAN byte cap (Table 3B sub 03, var. up to 19).</summary>
    public const int MaxRecipientPanBytes = 19;

    /// <summary>Recipient Name byte cap (Table 3A tag 59, var. up to 25).</summary>
    public const int MaxRecipientNameBytes = 25;

    /// <summary>Recipient City byte cap (Table 3A tag 60, var. up to 15).</summary>
    public const int MaxRecipientCityBytes = 15;

    /// <summary>Postal Code byte cap (Table 3A tag 61, var. up to 10).</summary>
    public const int MaxPostalCodeBytes = 10;

    /// <summary>Transaction Amount byte cap (Table 3A tag 54, var. up to 13).</summary>
    public const int MaxAmountBytes = 13;

    /// <summary>Additional-data sub-value byte cap (Table 4A, var. up to 25).</summary>
    public const int MaxAdditionalDataBytes = 25;

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)?$")]
    private static partial Regex AmountPattern();

    /// <summary>
    /// Validates the request against Table 3A/3B/4A shape and length rules
    /// (in UTF-8 bytes), then emits the ascending-order partial payload.
    /// Invalid input never reaches serialization — every failure is a
    /// <see cref="QrStructuralError"/> and the result is a failure.
    /// </summary>
    public static QrBuildResult Build(P2pQrRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<QrStructuralError>();

        if (request.InstitutionType.Length != 2
            || !request.InstitutionType.All(char.IsAsciiDigit)
            || int.Parse(request.InstitutionType, System.Globalization.CultureInfo.InvariantCulture) > MaxInstitutionType)
        {
            errors.Add(new QrStructuralError(
                QrStructuralReasonCode.Tag26InvalidType,
                "Institution Type (Tag 26 sub 01) must be two digits 00–05."));
        }

        if (request.InstitutionId.Length != 4 || !request.InstitutionId.All(char.IsAsciiDigit))
        {
            errors.Add(new QrStructuralError(
                QrStructuralReasonCode.Tag26InvalidId,
                "Institution ID (Tag 26 sub 02) must be exactly four digits."));
        }

        ValidateByteCap(request.RecipientPan, MaxRecipientPanBytes, "Recipient PAN (Tag 26 sub 03)", errors);
        ValidateByteCap(request.RecipientName, MaxRecipientNameBytes, "Recipient Name (Tag 59)", errors);
        ValidateByteCap(request.RecipientCity, MaxRecipientCityBytes, "Recipient City (Tag 60)", errors);

        if (request.CountryCode.Length != 2)
        {
            errors.Add(new QrStructuralError(
                QrStructuralReasonCode.QrLengthOverflow,
                "Country Code (Tag 58) must be exactly two characters."));
        }

        if (request.Currency.Length != 3 || !request.Currency.All(char.IsAsciiDigit))
        {
            errors.Add(new QrStructuralError(
                QrStructuralReasonCode.QrLengthOverflow,
                "Transaction Currency (Tag 53) must be exactly three digits."));
        }

        if (request.TransactionAmount is { } amount)
        {
            if (Utf8Length.ByteCount(amount) > MaxAmountBytes || !AmountPattern().IsMatch(amount))
            {
                errors.Add(new QrStructuralError(
                    QrStructuralReasonCode.QrLengthOverflow,
                    "Transaction Amount (Tag 54) must be digits with an optional single decimal point, up to 13 bytes."));
            }
        }

        if (request.PostalCode is { } postal && Utf8Length.ByteCount(postal) > MaxPostalCodeBytes)
        {
            errors.Add(new QrStructuralError(
                QrStructuralReasonCode.QrLengthOverflow,
                $"Postal Code (Tag 61) exceeds {MaxPostalCodeBytes} UTF-8 bytes."));
        }

        if (request.CustomerLabel is { } label && Utf8Length.ByteCount(label) > MaxAdditionalDataBytes)
        {
            errors.Add(new QrStructuralError(
                QrStructuralReasonCode.QrLengthOverflow,
                $"Customer Label (Tag 62 sub 06) exceeds {MaxAdditionalDataBytes} UTF-8 bytes."));
        }

        if (request.PurposeOfTransaction is { } purpose && Utf8Length.ByteCount(purpose) > MaxAdditionalDataBytes)
        {
            errors.Add(new QrStructuralError(
                QrStructuralReasonCode.QrLengthOverflow,
                $"Purpose of Transaction (Tag 62 sub 08) exceeds {MaxAdditionalDataBytes} UTF-8 bytes."));
        }

        if (errors.Count > 0)
        {
            return QrBuildResult.Failure(errors);
        }

        var tag26 = string.Concat(
            new TlvField("00", BqrConstants.NpsbNetworkGuid, 0).ToTlv(),
            new TlvField("01", request.InstitutionType, 0).ToTlv(),
            new TlvField("02", request.InstitutionId, 0).ToTlv(),
            new TlvField("03", request.RecipientPan, 0).ToTlv());

        var fields = new List<(string Tag, string Value)>
        {
            ("00", BqrConstants.PayloadFormatIndicator),
            ("01", request.IsDynamic ? BqrConstants.DynamicInitiation : BqrConstants.StaticInitiation),
            ("26", tag26),
            ("52", BqrConstants.P2pMerchantCategoryCode),
            ("53", request.Currency),
        };

        if (request.TransactionAmount is not null)
        {
            fields.Add(("54", request.TransactionAmount));
        }

        fields.Add(("58", request.CountryCode));
        fields.Add(("59", request.RecipientName));
        fields.Add(("60", request.RecipientCity));

        if (request.PostalCode is not null)
        {
            fields.Add(("61", request.PostalCode));
        }

        var additional = new List<TlvField>();
        if (request.CustomerLabel is not null)
        {
            additional.Add(new TlvField("06", request.CustomerLabel, 0));
        }

        if (request.PurposeOfTransaction is not null)
        {
            additional.Add(new TlvField("08", request.PurposeOfTransaction, 0));
        }

        if (additional.Count > 0)
        {
            fields.Add(("62", string.Concat(additional.Select(f => f.ToTlv()))));
        }

        // Ascending by tag ID, guaranteed regardless of the order values were
        // assembled above (PRD §5.3 step 2: deterministic output).
        var partial = string.Concat(fields
            .OrderBy(f => f.Tag, StringComparer.Ordinal)
            .Select(f => new TlvField(f.Tag, f.Value, 0).ToTlv()));

        return QrBuildResult.Ok(new UnsignedQrPayload(
            partial,
            SignaturePayload.Reconstruct(request.RecipientName, request.RecipientPan)));
    }

    private static void ValidateByteCap(string value, int maxBytes, string fieldName, List<QrStructuralError> errors)
    {
        if (string.IsNullOrEmpty(value) || Utf8Length.ByteCount(value) > maxBytes)
        {
            errors.Add(new QrStructuralError(
                QrStructuralReasonCode.QrLengthOverflow,
                $"{fieldName} must be non-empty and at most {maxBytes} UTF-8 bytes."));
        }
    }
}

/// <summary>Result envelope for the builder: success payload or structural errors (never exceptions for expected failures).</summary>
public sealed class QrBuildResult
{
    private readonly UnsignedQrPayload? _value;

    private QrBuildResult(UnsignedQrPayload? value, IReadOnlyList<QrStructuralError> errors)
    {
        _value = value;
        Errors = errors;
    }

    public bool IsSuccess => _value is not null;

    public UnsignedQrPayload Value => _value is not null
        ? _value
        : throw new InvalidOperationException("Cannot read Value from a failed QrBuildResult.");

    public IReadOnlyList<QrStructuralError> Errors { get; }

    public static QrBuildResult Ok(UnsignedQrPayload value) => new(value, Array.Empty<QrStructuralError>());

    public static QrBuildResult Failure(IReadOnlyList<QrStructuralError> errors) => new(null, errors);
}
