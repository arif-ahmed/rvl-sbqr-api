using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SBQR.Modules.Tenancy.Api.Contracts;
using SBQR.Modules.Tenancy.Application.Commands.RegisterTenantApplication;
using SBQR.Modules.Tenancy.Application.Commands.ReinstateTenantApplication;
using SBQR.Modules.Tenancy.Application.Commands.SuspendTenantApplication;
using SBQR.Modules.Tenancy.Application.Queries.ListTenantApplications;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Api.Controllers;

/// <summary>
/// HTTP surface for <c>/v1/admin/tenants/{tenantId}/applications</c>. Story 8
/// (FR-AUTH-002) added the per-tenant mobile-app allow-list: register, list,
/// suspend, reinstate. Lives in its own controller so the
/// <c>tenant_configurations</c>-provisioning surface (still on
/// <see cref="TenantsController"/>) and the application allow-list surface can
/// evolve independently.
///
/// <para>
/// Onboarding-order rule (operational): apps must be registered before they
/// are shipped — a shipped app whose row does not exist fails closed with
/// <c>invalid_client / package_mismatch</c> on every device. Suspended /
/// terminated tenants may not register new apps (mirrors the credential
/// provisioning admission rule).
/// </para>
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/admin/tenants/{tenantId:guid}/applications")]
[Authorize(Policy = PolicyNames.AdminCredentialTree)]
[ApiExplorerSettings(GroupName = "internal")]
[Produces("application/json")]
public sealed class TenantApplicationsController : ControllerBase
{
    private readonly ISender _mediator;

    public TenantApplicationsController(ISender mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    /// <summary>
    /// <c>POST /v1/admin/tenants/{tenantId}/applications</c> — register a new
    /// mobile-app allow-list row on behalf of the owning tenant. Returns
    /// <c>201 Created</c> with the freshly assigned id and the registered
    /// values. Duplicate <c>(platform, package_id)</c> → <c>409</c>.
    /// </summary>
    /// <param name="tenantId">Tenant identifier from the route.</param>
    /// <param name="request">Inbound JSON DTO.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost]
    [ProducesResponseType(typeof(TenantApplicationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterAsync(
        [FromRoute] Guid tenantId,
        [FromBody] RegisterTenantApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.TryParse<TenantApplicationPlatform>(request.Platform, ignoreCase: true, out var platform)
            || !Enum.IsDefined(platform))
        {
            return ValidationProblem(
                $"Unknown platform '{request.Platform}'. " +
                $"Allowed: {string.Join(", ", Enum.GetNames<TenantApplicationPlatform>())}.");
        }

        var command = new RegisterTenantApplicationCommand(
            TenantId: new TenantId(tenantId),
            Platform: platform,
            PackageId: request.PackageId);

        var result = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result);
        }

        var created = result.Value;
        var response = new TenantApplicationResponse(
            TenantApplicationId: created.TenantApplicationId.Value,
            TenantId: created.TenantId.Value,
            Platform: created.Platform switch
            {
                TenantApplicationPlatform.Android => "ANDROID",
                TenantApplicationPlatform.Ios => "IOS",
                _ => created.Platform.ToString().ToUpperInvariant(),
            },
            PackageId: created.PackageId,
            Status: "ACTIVE",
            IsActive: true,
            CreatedAt: created.RegisteredAt,
            ModifiedAt: null);

        // 201 Created with the registered row in the body — no Location header.
        // There is no single-application GET (only the list endpoint), so a
        // CreatedAtAction-style redirect target doesn't exist; returning the
        // body directly avoids depending on Asp.Versioning's route-value
        // resolution for a Location header that would just point back at the list.
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// <c>GET /v1/admin/tenants/{tenantId}/applications</c> — list every
    /// registered application for the tenant, ordered by
    /// <c>(platform, package_id)</c>. Returns <c>200 OK</c> with an array
    /// (possibly empty) when the tenant exists, or <c>404 Not Found</c> when
    /// the tenant id is unknown. Pure projection — no state change, no
    /// audit row.
    /// </summary>
    /// <param name="tenantId">Tenant identifier from the route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TenantApplicationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(
        [FromRoute] Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator
            .Send(new ListTenantApplicationsQuery(new TenantId(tenantId)), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result);
        }

        var projected = result.Value
            .Select(a => new TenantApplicationResponse(
                TenantApplicationId: a.TenantApplicationId,
                TenantId: a.TenantId,
                Platform: a.Platform,
                PackageId: a.PackageId,
                Status: a.Status,
                IsActive: a.IsActive,
                CreatedAt: a.CreatedAt,
                ModifiedAt: a.ModifiedAt))
            .ToArray();

        return Ok(projected);
    }

    /// <summary>
    /// <c>POST /v1/admin/tenants/{tenantId}/applications/{applicationId}/suspend</c>
    /// — per-app kill switch. Flips the row to <c>SUSPENDED</c>; sibling apps,
    /// the owning tenant, and its credentials are untouched
    /// (FR-AUTH-002 §5.6 BR4). Returns <c>200 OK</c> with the post-transition
    /// snapshot.
    /// </summary>
    /// <param name="tenantId">Tenant identifier from the route.</param>
    /// <param name="applicationId">Tenant-application identifier from the route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{applicationId:guid}/suspend")]
    [ProducesResponseType(typeof(TenantApplicationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SuspendAsync(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var command = new SuspendTenantApplicationCommand(
            TenantId: new TenantId(tenantId),
            TenantApplicationId: new TenantApplicationId(applicationId));

        var result = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result);
        }

        return Ok(ProjectFromSuspend(result.Value));
    }

    /// <summary>
    /// <c>POST /v1/admin/tenants/{tenantId}/applications/{applicationId}/reinstate</c>
    /// — undo the per-app kill switch. Flips the row back to <c>ACTIVE</c>;
    /// sibling apps, the owning tenant, and its credentials are untouched.
    /// Returns <c>200 OK</c> with the post-transition snapshot.
    /// </summary>
    /// <param name="tenantId">Tenant identifier from the route.</param>
    /// <param name="applicationId">Tenant-application identifier from the route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{applicationId:guid}/reinstate")]
    [ProducesResponseType(typeof(TenantApplicationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReinstateAsync(
        [FromRoute] Guid tenantId,
        [FromRoute] Guid applicationId,
        CancellationToken cancellationToken)
    {
        var command = new ReinstateTenantApplicationCommand(
            TenantId: new TenantId(tenantId),
            TenantApplicationId: new TenantApplicationId(applicationId));

        var result = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapFailure(result);
        }

        var r = result.Value;
        return Ok(new TenantApplicationResponse(
            TenantApplicationId: r.TenantApplicationId.Value,
            TenantId: r.TenantId.Value,
            Platform: r.Platform switch
            {
                TenantApplicationPlatform.Android => "ANDROID",
                TenantApplicationPlatform.Ios => "IOS",
                _ => r.Platform.ToString().ToUpperInvariant(),
            },
            PackageId: r.PackageId,
            Status: "ACTIVE",
            IsActive: true,
            CreatedAt: r.ReinstatedAt,
            ModifiedAt: r.ReinstatedAt));
    }

    private static TenantApplicationResponse ProjectFromSuspend(SuspendTenantApplicationResult r) =>
        new(
            TenantApplicationId: r.TenantApplicationId.Value,
            TenantId: r.TenantId.Value,
            Platform: r.Platform switch
            {
                TenantApplicationPlatform.Android => "ANDROID",
                TenantApplicationPlatform.Ios => "IOS",
                _ => r.Platform.ToString().ToUpperInvariant(),
            },
            PackageId: r.PackageId,
            Status: "SUSPENDED",
            IsActive: true,
            CreatedAt: r.SuspendedAt,
            ModifiedAt: r.SuspendedAt);

    /// <summary>
    /// Map a handler failure to the matching HTTP status, mirroring the
    /// <see cref="TenantsController.MapLifecycleFailure{T}"/> helper used by
    /// the suspend / reactivate / activate / terminate endpoints.
    /// </summary>
    private ActionResult MapFailure<T>(Result<T> result) => result.ErrorCode switch
    {
        ErrorCode.ValidationFailed => ValidationProblem(result.ErrorMessage),
        ErrorCode.NotFound => NotFound(new ProblemDetails
        {
            Title = "Tenant application not found",
            Detail = result.ErrorMessage,
            Status = StatusCodes.Status404NotFound,
        }),
        ErrorCode.InvariantViolation => Conflict(new ProblemDetails
        {
            Title = "Tenant application invariant violated",
            Detail = result.ErrorMessage,
            Status = StatusCodes.Status409Conflict,
        }),
        _ => Problem(
            title: "Tenant application action failed",
            detail: result.ErrorMessage,
            statusCode: StatusCodes.Status400BadRequest),
    };
}
