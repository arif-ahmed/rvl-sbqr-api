using MediatR;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.QrGeneration.Application.Commands.GenerateDynamicQr;

/// <summary>
/// Dynamic QR issuance — Tag 01 = "12" (spec §2.2.2 + Table 3A). Tag 54
/// (Transaction Amount) is <strong>required</strong> on the dynamic path
/// and is therefore a non-nullable parameter on this command — the
/// absence is enforced at compile time, not by a runtime check.
/// </summary>
public sealed record GenerateDynamicQrCommand(
    string TransactionAmount,
    string RecipientName,
    string RecipientCity,
    string RecipientPan,
    string? PostalCode = null,
    string? CustomerLabel = null,
    string? PurposeOfTransaction = null,
    string? IdempotencyKey = null)
    : IRequest<Result<GenerateQrResult>>, IRecipientQrFields;
