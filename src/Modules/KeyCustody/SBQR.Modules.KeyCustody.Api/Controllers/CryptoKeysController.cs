using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SBQR.Modules.KeyCustody.Api.Contracts;
using SBQR.Modules.KeyCustody.Application.Commands.GenerateOrAdoptCryptoKey;
using SBQR.Modules.KeyCustody.Application.Commands.RotateCryptoKey;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.Modules.KeyCustody.Application.Queries.GetActiveCryptoKey;
using SBQR.Modules.KeyCustody.Application.Queries.ListCryptoKeys;
using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.KeyCustody.Api.Controllers;

/// <summary>
/// HTTP surface for tenant signing-key lifecycle: mint (generate/adopt),
/// rotate, and inspect. This is the first HTTP surface KeyCustody exposes —
/// key generation/adoption/rotation previously had no endpoint at all; the
/// write-side machinery (aggregate, repository, generator, validator, vault)
/// now lives entirely in this module (moved from Tenancy).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/crypto-keys")]
[Authorize(Policy = PolicyNames.KeyAdmin)]
[ApiExplorerSettings(GroupName = "internal")]
[Produces("application/json")]
public sealed class CryptoKeysController : ControllerBase
{
    private readonly ISender _mediator;

    public CryptoKeysController(ISender mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// <c>POST /v1/crypto-keys</c> — mint the tenant's first signing key.
    /// <c>201 Created</c> on success. <c>404</c> if the tenant does not
    /// exist; <c>409</c> if the tenant already has an ACTIVE key (rotate via
    /// <c>PUT</c> instead); <c>400</c> for malformed/inconsistent adopt PEMs.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CryptoKeySummary), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateCryptoKeyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.TryParse<CryptoKeyMode>(request.Mode, ignoreCase: true, out var mode)
            || !Enum.IsDefined(mode))
        {
            return ValidationProblem(
                $"Unknown mode '{request.Mode}'. Allowed: {string.Join(", ", Enum.GetNames<CryptoKeyMode>())}.");
        }

        var command = new GenerateOrAdoptCryptoKeyCommand(
            TenantId: request.TenantId,
            Mode: mode,
            PrivateKeyPem: request.PrivateKeyPem);

        var result = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result);
        }

        return CreatedAtRoute(
            routeName: nameof(GetActiveAsync),
            routeValues: new { tenantId = result.Value.TenantId },
            value: result.Value);
    }

    /// <summary>
    /// <c>PUT /v1/crypto-keys/{tenantId}</c> — rotate the tenant's signing
    /// key (retire ACTIVE, mint v+1). Server-generates only. Skeleton: the
    /// handler currently returns a stub failure.
    /// </summary>
    [HttpPut("{tenantId:guid}")]
    [ProducesResponseType(typeof(CryptoKeySummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RotateAsync(
        [FromRoute] Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator
            .Send(new RotateCryptoKeyCommand(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? MapFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// <c>GET /v1/crypto-keys/{tenantId}/active</c> — the tenant's
    /// currently ACTIVE signing key. <c>404</c> if none.
    /// </summary>
    [HttpGet("{tenantId:guid}/active", Name = nameof(GetActiveAsync))]
    [ProducesResponseType(typeof(CryptoKeySummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActiveAsync(
        [FromRoute] Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator
            .Send(new GetActiveCryptoKeyQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? MapFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// <c>GET /v1/crypto-keys/{tenantId}</c> — the tenant's full signing-key
    /// version history (every status), paged and optionally status-filtered.
    /// </summary>
    [HttpGet("{tenantId:guid}", Name = nameof(GetForTenantAsync))]
    [ProducesResponseType(typeof(PagedResult<CryptoKeySummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetForTenantAsync(
        [FromRoute] Guid tenantId,
        [FromQuery] ListCryptoKeysRequest request,
        CancellationToken cancellationToken)
        => await ListAsync(tenantId, request, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// <c>GET /v1/crypto-keys</c> — paged list across all tenants,
    /// optionally status-filtered. Admin/reporting surface.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CryptoKeySummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(
        [FromQuery] ListCryptoKeysRequest request,
        CancellationToken cancellationToken)
        => await ListAsync(tenantId: null, request, cancellationToken).ConfigureAwait(false);

    private async Task<IActionResult> ListAsync(
        Guid? tenantId,
        ListCryptoKeysRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CryptoKeyStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<CryptoKeyStatus>(request.Status, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return ValidationProblem(
                    $"Unknown status '{request.Status}'. " +
                    $"Allowed: {string.Join(", ", Enum.GetNames<CryptoKeyStatus>())}.");
            }

            status = parsed;
        }

        var query = new ListCryptoKeysQuery(
            TenantId: tenantId,
            Status: status,
            Page: request.Page,
            PageSize: request.PageSize);

        var result = await _mediator.Send(query, cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? MapFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// Map a handler failure to the matching HTTP status. Generic over the
    /// success payload so callers don't have to box through <c>object</c>.
    /// </summary>
    private ActionResult MapFailure<T>(Result<T> result) => result.ErrorCode switch
    {
        ErrorCode.ValidationFailed => ValidationProblem(result.ErrorMessage),
        ErrorCode.NotFound => NotFound(new ProblemDetails
        {
            Title = "Signing key not found",
            Detail = result.ErrorMessage,
            Status = StatusCodes.Status404NotFound,
        }),
        ErrorCode.InvariantViolation => Conflict(new ProblemDetails
        {
            Title = "Signing key invariant violated",
            Detail = result.ErrorMessage,
            Status = StatusCodes.Status409Conflict,
        }),
        _ => Problem(
            title: "Signing key operation failed",
            detail: result.ErrorMessage,
            statusCode: StatusCodes.Status400BadRequest),
    };
}
