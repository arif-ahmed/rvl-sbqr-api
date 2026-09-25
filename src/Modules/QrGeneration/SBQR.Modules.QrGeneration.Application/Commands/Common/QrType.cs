namespace SBQR.Modules.QrGeneration.Application.Commands;

/// <summary>
/// The two QR modes the spec recognises (spec §2.2.1, §2.2.2, Table 3A
/// Tag 01). Stored as <c>"STATIC"</c> / <c>"DYNAMIC"</c> in the
/// <c>qr_generations.qr_type</c> column and the audit row's
/// <c>qr_type</c> metadata — the wire values match the enum name.
/// </summary>
public enum QrType
{
    Static = 0,
    Dynamic = 1,
}

internal static class QrTypeExtensions
{
    public static string ToWireValue(this QrType qrType) => qrType switch
    {
        QrType.Static => "STATIC",
        QrType.Dynamic => "DYNAMIC",
        _ => throw new ArgumentOutOfRangeException(nameof(qrType), qrType, "Unknown QR type."),
    };
}
