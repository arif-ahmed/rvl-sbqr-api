using MediatR;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.QrGeneration.Application.Commands.GenerateDynamicQr;

/// <summary>
/// Dynamic QR issuance handler — thin adapter that translates the public
/// command into a typed call on <see cref="IssueDynamicQrService"/>. The
/// service is the single place where the static/dynamic distinction
/// surfaces; the shared <see cref="Common.QrIssuancePipeline"/> does the
/// rest.
/// </summary>
public sealed class GenerateDynamicQrCommandHandler
    : IRequestHandler<GenerateDynamicQrCommand, Result<GenerateQrResult>>
{
    private readonly IssueDynamicQrService _service;

    public GenerateDynamicQrCommandHandler(IssueDynamicQrService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public Task<Result<GenerateQrResult>> Handle(
        GenerateDynamicQrCommand request,
        CancellationToken cancellationToken) =>
        _service.IssueAsync(
            transactionAmount: request.TransactionAmount,
            recipientName: request.RecipientName,
            recipientCity: request.RecipientCity,
            recipientPan: request.RecipientPan,
            postalCode: request.PostalCode,
            customerLabel: request.CustomerLabel,
            purposeOfTransaction: request.PurposeOfTransaction,
            idempotencyKey: request.IdempotencyKey,
            cancellationToken: cancellationToken);
}
