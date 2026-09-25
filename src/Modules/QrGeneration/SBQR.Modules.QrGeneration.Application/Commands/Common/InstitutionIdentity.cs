namespace SBQR.Modules.QrGeneration.Application.Commands;

/// <summary>
/// The institution identity stamped into Tag 26 of the QR payload. Always
/// sourced from the resolved tenant record — never caller-supplied — so the
/// issuer can't spoof another institution's code (spec Annex A).
/// </summary>
internal readonly record struct InstitutionIdentity(
    string InstitutionCode,
    string InstitutionType,
    string InstitutionId);
