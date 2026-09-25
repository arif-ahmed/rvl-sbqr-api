using System.Text;

namespace SBQR.SharedKernel.QrCodec;

/// <summary>
/// CRC-16/CCITT-FALSE — the EMV QRCPS convention incorporated by reference by
/// the BanglaQR P2P specification (Table 3A, tag "63"). Parameter set:
/// polynomial 0x1021, initial value 0xFFFF, no input/output reflection, no
/// final XOR; the CRC is computed over the payload's UTF-8 bytes up to and
/// including the literal "6304" prefix of the CRC tag itself
/// ([BB-CLARIFY #B7] is unconfirmed, so the parameter set is documented here
/// as the source of truth).
///
/// Pure function, no shared mutable state — safe to call from both the build
/// and parse paths.
/// </summary>
public static class Crc16CcittFalse
{
    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ushort)(i << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }

            table[i] = crc;
        }

        return table;
    }

    /// <summary>Computes the CRC over raw bytes and returns 4 uppercase hex characters, zero-padded.</summary>
    public static string Compute(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFF;
        foreach (var b in bytes)
        {
            crc = (ushort)((crc << 8) ^ Table[(crc >> 8) ^ b]);
        }

        return crc.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Computes the CRC over the UTF-8 encoding of <paramref name="text"/>.</summary>
    public static string Compute(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Compute(Encoding.UTF8.GetBytes(text));
    }
}
