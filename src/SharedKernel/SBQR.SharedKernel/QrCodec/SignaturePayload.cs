using System.Text;

namespace SBQR.SharedKernel.QrCodec;

/// <summary>
/// The one function that builds the exact byte sequence every Ed25519
/// signature in this system is computed over: the Recipient Name (Tag 59
/// value) concatenated with the Recipient PAN (Tag 26 sub-tag 03 value), no
/// separator, UTF-8-encoded (spec §2.5: "AreebaNawar" + "01711111111" →
/// "AreebaNawar01711111111").
///
/// Both the builder path (QrGeneration, pre-signing) and the parser path
/// (Verification, pre-verify) must call <see cref="Reconstruct"/> — no second
/// concatenation implementation may exist anywhere; a divergence between the
/// two sides is a silent, catastrophic interop bug.
///
/// Bytes only: this function never signs or verifies (that boundary belongs
/// to Key Custody / Verification).
/// </summary>
public static class SignaturePayload
{
    /// <summary>Concatenates name and PAN with no separator and UTF-8-encodes the result.</summary>
    public static byte[] Reconstruct(string recipientName, string recipientPan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientPan);

        // Concatenated then encoded once — equivalent to name-bytes ‖ pan-bytes
        // for UTF-8, and keeps a single allocation on the exact signed sequence.
        return Encoding.UTF8.GetBytes(string.Concat(recipientName, recipientPan));
    }
}
