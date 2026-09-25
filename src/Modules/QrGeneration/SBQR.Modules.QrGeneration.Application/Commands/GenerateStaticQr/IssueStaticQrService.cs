using SBQR.Modules.QrGeneration.Application.Commands.Common;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.QrGeneration.Application.Commands.GenerateStaticQr;

/// <summary>
/// Static-QR issuance service (spec §2.2.1, Tag 01 = "11"). The service
/// has no <c>transactionAmount</c> parameter — that absence is the entire
/// point of the static path: Tag 54 (Transaction Amount) is structurally
/// absent from a static QR because the amount is decided at pay time.
/// The shared <see cref="QrIssuancePipeline"/> does every other step
/// (admission gate, activation gate, codec, signing, persistence, audit,
/// fail-closed). This service is intentionally a one-line forwarder so the
/// static/dynamic split is enforceable at the call site (the controller)
/// and at the test seam (the integration test).
/// </summary>
public sealed class IssueStaticQrService
{
    private readonly QrIssuancePipeline _pipeline;

    public IssueStaticQrService(QrIssuancePipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public Task<Result<GenerateQrResult>> IssueAsync(
        string recipientName,
        string recipientCity,
        string recipientPan,
        string? postalCode,
        string? customerLabel,
        string? purposeOfTransaction,
        string? idempotencyKey,
        CancellationToken cancellationToken) =>
        _pipeline.IssueAsync(
            qrType: QrType.Static,
            recipientName: recipientName,
            recipientCity: recipientCity,
            recipientPan: recipientPan,
            transactionAmount: null,
            postalCode: postalCode,
            customerLabel: customerLabel,
            purposeOfTransaction: purposeOfTransaction,
            idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);
}
