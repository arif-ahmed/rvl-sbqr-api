using System.Text;

namespace SBQR.SharedKernel.QrCodec;

/// <summary>
/// The single place UTF-8 byte length is computed (epic-2 Story 3). All field
/// length checks in this module go through <see cref="ByteCount"/> — never
/// <c>string.Length</c> — so a Bangla-script value can never sneak past a
/// byte budget because its char count happened to fit.
/// </summary>
public static class Utf8Length
{
    /// <summary>Returns the UTF-8 byte count of <paramref name="value"/>.</summary>
    public static int ByteCount(string value) => Encoding.UTF8.GetByteCount(value);
}
