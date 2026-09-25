using System.Buffers.Text;
using System.Text;

namespace SBQR.SharedKernel.QrCodec;

/// <summary>
/// Final payload assembly (epic-2 Story 7): takes a builder partial payload
/// plus an already-computed 88-character Base64 signature and produces the
/// final QR string with Tags 80/81 inserted and the CRC appended — in that
/// order. The single <see cref="Finalize"/> entry point exists precisely so
/// no caller can append a CRC before the signature tags are in place
/// (load-bearing assertion L2: Tag 63 cannot precede Tags 80/81).
///
/// This module never generates or requests signatures — the input is produced
/// by Key Custody's signing provider.
/// </summary>
public static class QrPayloadFinalizer
{
    private const int SignatureLength = 88;
    private const int SignaturePartLength = 44;
    private const int RawSignatureBytes = 64;

    /// <summary>
    /// Inserts Tag 80 (GUID + first 44 Base64 chars) and Tag 81 (GUID + last
    /// 44 chars) after the partial payload's business tags, then appends
    /// "6304" + CRC-16/CCITT-FALSE computed over everything up to and
    /// including the "6304" prefix.
    /// </summary>
    /// <exception cref="QrFinalizeException">The signature is not exactly 88 standard-alphabet Base64 characters, or the partial payload contains tags ≥ 80.</exception>
    public static string Finalize(UnsignedQrPayload unsignedPayload, string signatureBase64)
    {
        ArgumentNullException.ThrowIfNull(unsignedPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureBase64);

        ValidateSignature(signatureBase64);

        var (fields, errors) = TlvTokenizer.Tokenize(unsignedPayload.PartialPayload);
        if (errors.Count > 0)
        {
            throw new QrFinalizeException(
                QrStructuralReasonCode.MalformedTlv,
                "The partial payload is not well-formed TLV; it must come from P2pQrBuilder.Build.");
        }

        if (fields.Any(f => string.CompareOrdinal(f.TagId, "80") >= 0))
        {
            throw new QrFinalizeException(
                QrStructuralReasonCode.MalformedTlv,
                "The partial payload must contain only business tags (below 80); signature and CRC tags are added by Finalize.");
        }

        var signaturePart1 = signatureBase64[..SignaturePartLength];
        var signaturePart2 = signatureBase64[SignaturePartLength..];

        // Tables 5A/5B: each signature tag is a template whose value is the
        // sub-TLV pair (sub 00 = NPSB GUID, sub 01 = 44-char Base64 part).
        var tag80 = new TlvField(
            "80",
            string.Concat(
                new TlvField("00", BqrConstants.NpsbNetworkGuid, 0).ToTlv(),
                new TlvField("01", signaturePart1, 0).ToTlv()),
            0).ToTlv();
        var tag81 = new TlvField(
            "81",
            string.Concat(
                new TlvField("00", BqrConstants.NpsbNetworkGuid, 0).ToTlv(),
                new TlvField("01", signaturePart2, 0).ToTlv()),
            0).ToTlv();

        var withSignature = string.Concat(unsignedPayload.PartialPayload, tag80, tag81);

        // CRC covers everything up to and including the literal "6304" —
        // appended last, per PRD §5.3 step 6.
        return string.Concat(withSignature, "6304", Crc16CcittFalse.Compute(withSignature + "6304"));
    }

    private static void ValidateSignature(string signatureBase64)
    {
        if (signatureBase64.Length != SignatureLength)
        {
            throw new QrFinalizeException(
                QrStructuralReasonCode.InvalidSignatureLength,
                $"Signature must be exactly {SignatureLength} Base64 characters (Ed25519 64-byte signature), got {signatureBase64.Length}.");
        }

        // Standard Base64 alphabet only — Base64URL ('-', '_') is explicitly
        // forbidden for this field (PRD §7.1). '=' padding may only appear at the end.
        var paddedEnd = signatureBase64.AsSpan().TrimEnd('=');
        if (signatureBase64.Length - paddedEnd.Length > 2)
        {
            throw new QrFinalizeException(
                QrStructuralReasonCode.InvalidBase64Alphabet,
                "Signature has more than two '=' padding characters.");
        }

        foreach (var c in paddedEnd)
        {
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '/')
            {
                continue;
            }

            throw new QrFinalizeException(
                QrStructuralReasonCode.InvalidBase64Alphabet,
                $"Signature contains '{c}' — standard Base64 alphabet only (Base64URL '-'/'_' is forbidden).");
        }

        if (!Base64.IsValid(signatureBase64)
            || Convert.FromBase64String(signatureBase64).Length != RawSignatureBytes)
        {
            throw new QrFinalizeException(
                QrStructuralReasonCode.InvalidBase64Alphabet,
                $"Signature must decode to exactly {RawSignatureBytes} raw bytes (Ed25519).");
        }
    }
}

/// <summary>Thrown when the finalizer is handed an unusable signature or partial payload — caller error, not payload data error.</summary>
public sealed class QrFinalizeException(
    QrStructuralReasonCode code,
    string message) : Exception(message)
{
    /// <summary>The structural reason the input was rejected.</summary>
    public QrStructuralReasonCode Code { get; } = code;
}
