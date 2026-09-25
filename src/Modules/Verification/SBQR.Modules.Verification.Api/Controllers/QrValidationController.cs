using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SBQR.Modules.Verification.Application.Commands;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Verification.Api.Controllers;

/// <summary>
/// HTTP surface for QR verification. Sep-17 scope: the server-to-server
/// plane — tenant FI client credentials with scope <c>qr:validate</c>. That
/// auth mechanism is orthogonal to OpenAPI document placement: this
/// controller carries no <c>[ApiExplorerSettings(GroupName = "internal")]</c>,
/// so it flows into the <c>v1.public</c> document — see
/// <c>docs/superpowers/specs/2026-09-04-public-vs-internal-api-docs-design.md</c>.
/// A caller still needs a valid <c>qr:validate</c>-scoped bearer token from
/// <c>POST /v1/oauth/token</c>; being documented publicly does not make this
/// endpoint anonymous.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/qr")]
[Authorize(Policy = PolicyNames.QrValidate)]
[Produces("application/json")]
public sealed class QrValidationController : ControllerBase
{
    private readonly ISender _mediator;

    public QrValidationController(ISender mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// <c>POST /v1/qr/validate</c> — verify one raw QR payload string and
    /// return the verdict. Rejection verdicts are normal responses (200),
    /// not errors: the verdict IS the answer.
    /// </summary>
    /// <remarks>
    /// The request body carries only the raw QR payload string. The C6
    /// replay-guard <c>requestId</c> and <c>requestTimestamp</c> are
    /// server-generated inside the handler so the call site stays a single
    /// field for now. When the on-the-wire C6 contract returns, the
    /// controller accepts both shapes — server-side generation kicks in
    /// only when the client omits them.
    /// </remarks>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ValidateQrResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ValidateAsync(
        [FromBody] ValidateQrRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ValidateQrCommand(
                request.QrPayload,
                request.RequestId ?? Guid.NewGuid().ToString("N"),
                request.RequestTimestamp ?? DateTimeOffset.UtcNow),
            cancellationToken);

        return Ok(new ValidateQrResponse(
            result.Verdict.ToString(),
            result.TrustSource?.ToString(),
            result.ReasonCode,
            result.InstitutionCode,
            result.PayloadHash,
            result.RecipientName,
            result.RecipientPan,
            result.QrClassification));
    }
}

/// <summary>
/// Inbound JSON DTO. Only <c>QrPayload</c> is required for now — the
/// replay-guard fields <c>RequestId</c> and <c>RequestTimestamp</c> are
/// optional and server-generated when omitted.
/// </summary>
public sealed record ValidateQrRequest(
    string QrPayload,
    string? RequestId = null,
    DateTimeOffset? RequestTimestamp = null);

/// <summary>Outbound DTO: the verdict plus the decoded identity fields the payer app reviews.</summary>
public sealed record ValidateQrResponse(
    string Verdict,
    string? TrustSource,
    string? ReasonCode,
    string? InstitutionCode,
    string PayloadHash,
    string? RecipientName,
    string? RecipientPan,
    string QrClassification);
