using MediatR;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.QrGeneration.Application.Commands.GenerateStaticQr;

/// <summary>
/// Static QR issuance — Tag 01 = "11" (spec §2.2.1 + Table 3A). A static
/// QR encodes only the recipient identity; the transaction amount is
/// decided at pay time and is therefore <strong>not</strong> carried in
/// the payload (Tag 54 must be absent). The static endpoint and command
/// deliberately do <strong>not</strong> accept a <c>transactionAmount</c>
/// field — the absence is part of the contract, not an oversight.
/// </summary>
public sealed record GenerateStaticQrCommand(
    string RecipientName,
    string RecipientCity,
    string RecipientPan,
    string? PostalCode = null,
    string? CustomerLabel = null,
    string? PurposeOfTransaction = null,
    string? IdempotencyKey = null)
    : IRequest<Result<GenerateQrResult>>, IRecipientQrFields;
