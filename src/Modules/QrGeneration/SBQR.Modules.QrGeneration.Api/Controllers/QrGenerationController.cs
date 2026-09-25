using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SBQR.Modules.QrGeneration.Application.Commands;
using SBQR.Modules.QrGeneration.Application.Commands.GenerateDynamicQr;
using SBQR.Modules.QrGeneration.Application.Commands.GenerateStaticQr;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.QrGeneration.Api.Controllers;

/// <summary>
/// HTTP surface for QR generation. Sep-17 scope: the server-to-server plane
/// — tenant FI client credentials with scope <c>qr:generate</c> (the mobile
/// plane per blockers B1/B2 lands post-deadline). That auth mechanism is
/// orthogonal to OpenAPI document placement: this controller carries no
/// <c>[ApiExplorerSettings(GroupName = "internal")]</c>, so it flows into the
/// <c>v1.public</c> document — see
/// <c>docs/superpowers/specs/2026-09-04-public-vs-internal-api-docs-design.md</c>.
/// A caller still needs a valid <c>qr:generate</c>-scoped bearer token from
/// <c>POST /v1/oauth/token</c>; being documented publicly does not make this
/// endpoint anonymous.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/qr")]
[Authorize(Policy = PolicyNames.QrGenerate)]
[Produces("application/json")]
public sealed class QrGenerationController : ControllerBase
{
    private readonly ISender _mediator;

    public QrGenerationController(ISender mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// <c>POST /v1/qr/generate/static</c> — build, sign, and return one
    /// <strong>static</strong> P2P BanglaQR payload (Tag 01 = "11"). A static
    /// QR encodes only the recipient identity; the transaction amount is
    /// decided at pay time and is therefore <strong>not</strong> carried in
    /// the payload. Sending <c>transactionAmount</c> on this endpoint
    /// returns 400. 422 when the tenant has no ACTIVE signing key; 409 when
    /// the <c>Idempotency-Key</c> header was already used by this tenant.
    /// </summary>
    [HttpPost("generate/static")]
    [ProducesResponseType(typeof(GenerateQrResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GenerateStaticAsync(
        [FromBody] GenerateStaticQrRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GenerateStaticQrCommand(
            request.RecipientName,
            request.RecipientCity,
            request.RecipientPan,
            request.PostalCode,
            request.CustomerLabel,
            request.PurposeOfTransaction,
            idempotencyKey), cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// <c>POST /v1/qr/generate/dynamic</c> — build, sign, and return one
    /// <strong>dynamic</strong> P2P BanglaQR payload (Tag 01 = "12"). A
    /// dynamic QR carries the transaction amount in Tag 54 — that field is
    /// <strong>required</strong> on this endpoint (400 when missing or
    /// non-numeric). 422 when the tenant has no ACTIVE signing key; 409
    /// when the <c>Idempotency-Key</c> header was already used by this
    /// tenant.
    /// </summary>
    [HttpPost("generate/dynamic")]
    [ProducesResponseType(typeof(GenerateQrResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GenerateDynamicAsync(
        [FromBody] GenerateDynamicQrRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GenerateDynamicQrCommand(
            request.TransactionAmount,
            request.RecipientName,
            request.RecipientCity,
            request.RecipientPan,
            request.PostalCode,
            request.CustomerLabel,
            request.PurposeOfTransaction,
            idempotencyKey), cancellationToken);

        return ToActionResult(result);
    }

    /// <summary>
    /// Shared error → status mapping for both endpoints. The two endpoints
    /// are deliberately identical from an error-contract perspective so
    /// client SDKs share the same error-handling code path.
    /// </summary>
    private ObjectResult ToActionResult(Result<GenerateQrResult> result)
    {
        if (result.IsSuccess)
        {
            return StatusCode(StatusCodes.Status201Created, new GenerateQrResponse(
                result.Value.QrPayload,
                result.Value.PayloadHash,
                result.Value.QrType,
                result.Value.SignatureKeyVersion));
        }

        return result.ErrorCode switch
        {
            ErrorCode.Unauthenticated => Unauthorized(new { error = result.ErrorCode, message = result.ErrorMessage }),
            ErrorCode.NotFound => NotFound(new { error = result.ErrorCode, message = result.ErrorMessage }),
            ErrorCode.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                new { error = "TENANT_NOT_ACTIVE", message = result.ErrorMessage }),
            ErrorCode.ValidationFailed => BadRequest(new { error = result.ErrorCode, message = result.ErrorMessage }),
            ErrorCode.InvalidState => UnprocessableEntity(new { error = "KEY_NOT_ACTIVE", message = result.ErrorMessage }),
            ErrorCode.Conflict => Conflict(new { error = "DUPLICATE_IDEMPOTENCY_KEY", message = result.ErrorMessage }),
            // Distinct from a generic 500 so ops can tell "key/HSM problem" apart
            // from an unhandled software bug at a glance. Still a 500 — the
            // caller cannot fix this by changing their request.
            ErrorCode.SigningFailed => StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "SIGNING_FAILED", message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "GENERATION_FAILED", message = result.ErrorMessage }),
        };
    }
}

/// <summary>Inbound JSON DTO for static QR generation (Tag 01 = "11").</summary>
public sealed record GenerateStaticQrRequest(
    string RecipientName,
    string RecipientCity,
    string RecipientPan,
    string? PostalCode = null,
    string? CustomerLabel = null,
    string? PurposeOfTransaction = null);

/// <summary>Inbound JSON DTO for dynamic QR generation (Tag 01 = "12").</summary>
public sealed record GenerateDynamicQrRequest(
    string TransactionAmount,
    string RecipientName,
    string RecipientCity,
    string RecipientPan,
    string? PostalCode = null,
    string? CustomerLabel = null,
    string? PurposeOfTransaction = null);

/// <summary>Outbound DTO: the signed QR payload (returned once, never persisted) plus metadata.</summary>
public sealed record GenerateQrResponse(
    string QrPayload,
    string PayloadHash,
    string QrType,
    int SignatureKeyVersion);