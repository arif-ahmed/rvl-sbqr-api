using MediatR;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.QrGeneration.Application.Commands.GenerateStaticQr;

/// <summary>
/// Static QR issuance handler — thin adapter that translates the public
/// command into a typed call on <see cref="IssueStaticQrService"/>. The
/// service is the single place where the static/dynamic distinction
/// surfaces; the shared <see cref="Common.QrIssuancePipeline"/> does the
/// rest.
/// </summary>
public sealed class GenerateStaticQrCommandHandler
    : IRequestHandler<GenerateStaticQrCommand, Result<GenerateQrResult>>
{
    private readonly IssueStaticQrService _service;

    public GenerateStaticQrCommandHandler(IssueStaticQrService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public Task<Result<GenerateQrResult>> Handle(
        GenerateStaticQrCommand request,
        CancellationToken cancellationToken) =>
        _service.IssueAsync(
            recipientName: request.RecipientName,
            recipientCity: request.RecipientCity,
            recipientPan: request.RecipientPan,
            postalCode: request.PostalCode,
            customerLabel: request.CustomerLabel,
            purposeOfTransaction: request.PurposeOfTransaction,
            idempotencyKey: request.IdempotencyKey,
            cancellationToken: cancellationToken);
}
