namespace SBQR.Modules.QrGeneration.Application.Commands;

/// <summary>
/// The generated QR payload plus the audit metadata. The payload string is
/// returned once and never persisted (decision 2026-09-08); the payload hash
/// flows only to the API response and to the audit row's
/// <c>resource_id</c> for correlation, never to a QR-generation column.
/// </summary>
public sealed record GenerateQrResult(
    string QrPayload,
    string PayloadHash,
    int SignatureKeyVersion,
    string QrType);
