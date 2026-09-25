using SBQR.Modules.QrGeneration.Application.Commands.Common;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.QrGeneration.Application.Commands.GenerateDynamicQr;

/// <summary>
/// Dynamic-QR issuance service (spec §2.2.2, Tag 01 = "12"). Tag 54
/// (Transaction Amount) is <strong>required</strong> on the dynamic path;
/// the parameter is non-nullable on this service so the request cannot
/// reach the codec without an amount. The shared
/// <see cref="QrIssuancePipeline"/> does every other step (admission
/// gate, activation gate, codec, signing, persistence, audit,
/// fail-closed). This service is intentionally a one-line forwarder.
/// </summary>
public sealed class IssueDynamicQrService
{
    private readonly QrIssuancePipeline _pipeline;

    public IssueDynamicQrService(QrIssuancePipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public Task<Result<GenerateQrResult>> IssueAsync(
        string transactionAmount,
        string recipientName,
        string recipientCity,
        string recipientPan,
        string? postalCode,
        string? customerLabel,
        string? purposeOfTransaction,
        string? idempotencyKey,
        CancellationToken cancellationToken) =>
        _pipeline.IssueAsync(
            qrType: QrType.Dynamic,
            recipientName: recipientName,
            recipientCity: recipientCity,
            recipientPan: recipientPan,
            transactionAmount: transactionAmount,
            postalCode: postalCode,
            customerLabel: customerLabel,
            purposeOfTransaction: purposeOfTransaction,
            idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);
}
