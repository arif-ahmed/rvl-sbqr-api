using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SBQR.Modules.Tenancy.Api.Contracts;
using SBQR.Modules.Tenancy.Application.Commands.ActivateTenant;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Tenancy.Application.Commands.ProvisionTenantConfiguration;
using SBQR.Modules.Tenancy.Application.Commands.ReactivateTenant;
using SBQR.Modules.Tenancy.Application.Commands.SuspendTenant;
using SBQR.Modules.Tenancy.Application.Commands.TerminateTenant;
using SBQR.Modules.Tenancy.Application.Contracts;
using SBQR.Modules.Tenancy.Application.Contracts.Mapping;
using SBQR.Modules.Tenancy.Application.Queries.GetTenantById;
using SBQR.Modules.Tenancy.Application.Queries.ListTenants;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Api.Controllers;

/// <summary>
/// HTTP surface for the <c>/v1/admin/tenants</c> route tree. Story 4 added the
/// suspend / reactivate endpoints alongside the institute-register flow from
/// Story 2/3. The <c>GetTenantById</c> action fills the read-side that used to
/// be a 501 placeholder so the <c>CreatedAtAction</c> Location header from
/// <see cref="RegisterAsync"/> resolves to a real implementation.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/admin/tenants")]
[Authorize(Policy = PolicyNames.AdminCredentialTree)]
[ApiExplorerSettings(GroupName = "internal")]
[Produces("application/json")]
public sealed class TenantsController : ControllerBase
{
    private readonly ISender _mediator;
    private readonly ITenantRepository _tenants;

    /// <summary>
    /// Construct the controller with the MediatR sender and the tenant
    /// repository (used to re-load the post-transition aggregate for the
    /// response body). Handlers are registered globally by the host
    /// (<c>SBQR.Api/Program.cs</c>) from the Tenancy Application assembly via
    /// <c>AddMediatR(cfg =&gt; cfg.RegisterServicesFromAssemblies(...))</c>.
    /// </summary>
    public TenantsController(ISender mediator, ITenantRepository tenants)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
    }

    /// <summary>
    /// <c>POST /v1/admin/tenants</c> — create a new tenant in
    /// <see cref="TenantStatus.Pending"/>. Client-configuration issuance and
    /// signing-key generation are intentionally out of scope here: the
    /// initial FI configuration is minted on demand by
    /// <see cref="ProvisionConfigurationAsync"/>, and signing-key generation
    /// will land as its own dedicated endpoint.
    ///
    /// <para>On success, returns <c>201 Created</c> with a <c>Location</c>
    /// header pointing at <see cref="GetTenantById"/> and a body containing the
    /// tenant summary only — no <c>apiConfiguration</c>, no <c>cryptoKey</c>.</para>
    ///
    /// <para>Validation failures surface as 400 (handled by the global
    /// pipeline), uniqueness collisions as 409
    /// (<see cref="ErrorCode.InvariantViolation"/>), and missing
    /// authorization as 401/403.</para>
    /// </summary>
    /// <param name="request">Inbound JSON DTO.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterAsync(
        [FromBody] RegisterTenantRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateTenantCommand(
            InstitutionName: request.InstitutionName,
            InstitutionCode: request.InstitutionCode);

        var result = await _mediator
            .Send(command, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.ErrorCode switch
            {
                ErrorCode.ValidationFailed => ValidationProblem(result.ErrorMessage),
                ErrorCode.InvariantViolation => Conflict(new ProblemDetails
                {
                    Title = "Tenant invariant violated",
                    Detail = result.ErrorMessage,
                    Status = StatusCodes.Status409Conflict,
                }),
                _ => Problem(
                    title: "Tenant register failed",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status400BadRequest),
            };
        }

        var created = result.Value;
        var response = new TenantResponse(
            TenantId: created.TenantId.Value,
            InstitutionName: request.InstitutionName,
            InstitutionCode: request.InstitutionCode,
            Status: nameof(TenantStatus.Pending),
            IsActive: true);

        // The controller route carries a v{version:apiVersion} segment; Asp.Versioning
        // does not resolve that segment from ambient route values during link
        // generation, so it must be passed explicitly or CreatedAtAction throws
        // "No route matches the supplied values" while building the Location header.
        return CreatedAtAction(
            actionName: nameof(GetTenantById),
            routeValues: new { id = created.TenantId.Value, version = RouteData.Values["version"] },
            value: response);
    }

    /// <summary>
    /// <c>POST /v1/admin/tenants/{id}/tenant-configuration</c> — provision the tenant's
    /// initial FI client configuration through the IdentityAccess Contracts
    /// seam. Mints a <c>{institution_code}-{8hex}</c> <c>client_id</c> and a
    /// 256-bit <c>client_secret</c> (the tenant authenticates with both at
    /// <c>POST /v1/oauth/token</c>).
    ///
    /// <para>Returns <c>201 Created</c> with the plaintext
    /// <c>clientSecret</c> shown <b>exactly once</b> — only its Argon2id hash
    /// is persisted, and there is no re-display endpoint. The FI must store
    /// it immediately.</para>
    ///
    /// <para>Admission rules: <see cref="TenantStatus.Pending"/> and
    /// <see cref="TenantStatus.Active"/> tenants may provision (the token
    /// endpoint deliberately lets Pending tenants authenticate so
    /// registration is exercisable end-to-end before BB activation);
    /// Suspended / Terminated tenants are refused. Re-provisioning while an
    /// active configuration exists returns <c>409</c> — rotation is a separate
    /// future flow. Validation failures surface as 400,
    /// <see cref="ErrorCode.NotFound"/> as 404, and
    /// <see cref="ErrorCode.InvariantViolation"/> as 409.</para>
    /// </summary>
    /// <param name="id">Tenant identifier from the route.</param>
    /// <param name="request">Optional body carrying the admin's QR capability
    ///     choice (defaults: both true). The endpoint still accepts an empty
    ///     body — backwards-compatible with the original no-body behaviour —
    ///     in which case both capability flags default to <c>true</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/tenant-configuration")]
    [ProducesResponseType(typeof(ProvisionTenantConfigurationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProvisionConfigurationAsync(
        [FromRoute] Guid id,
        [FromBody] ProvisionTenantConfigurationRequest? request,
        CancellationToken cancellationToken)
    {
        var command = new ProvisionTenantConfigurationCommand(
            TenantId: new TenantId(id),
            IsQrGenerationAllowed: request?.IsQrGenerationAllowed ?? true,
            IsQrValidationAllowed: request?.IsQrValidationAllowed ?? true);

        var result = await _mediator
            .Send(command, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapLifecycleFailure(result);
        }

        var configuration = result.Value;
        var response = new ProvisionTenantConfigurationResponse(
            TenantId: configuration.TenantId.Value,
            CredentialId: configuration.CredentialId,
            ClientId: configuration.ClientId,
            ClientSecret: configuration.ClientSecret,
            ExpiresAt: configuration.ExpiresAt);

        // Location points at the owning tenant — the configuration itself has no
        // read endpoint (the secret cannot be re-displayed, so a GET would
        // only ever echo the client_id the caller just received). version is
        // passed explicitly for the same reason as RegisterAsync above.
        return CreatedAtAction(
            actionName: nameof(GetTenantById),
            routeValues: new { id = configuration.TenantId.Value, version = RouteData.Values["version"] },
            value: response);
    }

    /// <summary>
    /// <c>POST /v1/admin/tenants/{id}/suspend</c> — move a tenant from
    /// <see cref="TenantStatus.Active"/> or <see cref="TenantStatus.Pending"/>
    /// into <see cref="TenantStatus.Suspended"/>. Cascades to the tenant's
    /// <c>api_credentials</c> rows (via the IdentityAccess Contracts seam) and
    /// to its <see cref="CryptoKey"/> so the token endpoint and signing
    /// service can rely on the child rows' status.
    ///
    /// <para>Returns <c>200 OK</c> with the post-transition
    /// <see cref="TenantResponse"/>. Validation failures surface as 400,
    /// <see cref="ErrorCode.InvariantViolation"/> (self-transition / terminal
    /// state) as 409, and <see cref="ErrorCode.NotFound"/> (no row with that
    /// id) as 404.</para>
    /// </summary>
    /// <param name="id">Tenant identifier from the route.</param>
    /// <param name="request">Inbound JSON DTO (optional reason).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SuspendAsync(
        [FromRoute] Guid id,
        [FromBody] SuspendTenantRequest? request,
        CancellationToken cancellationToken)
    {
        var command = new SuspendTenantCommand(
            TenantId: new TenantId(id),
            Reason: request?.Reason);

        var result = await _mediator
            .Send(command, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapLifecycleFailure(result);
        }

        // Re-load the aggregate so the response body reflects the post-transition
        // status + child cascades (the handler returns only the metadata).
        var tenant = await _tenants
            .GetByIdAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return tenant is null
            ? StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Tenant row missing after suspend",
                Detail = "The tenant was successfully suspended but could not be reloaded.",
                Status = StatusCodes.Status500InternalServerError,
            })
            : Ok(BuildTenantResponse(tenant));
    }

    /// <summary>
    /// <c>POST /v1/admin/tenants/{id}/terminate</c> — move the tenant into the
    /// terminal <see cref="TenantStatus.Terminated"/> state and set
    /// <see cref="Tenant.IsActive"/> = <c>false</c> atomically. One-way trip:
    /// re-terminating returns 409. Cascades to the active
    /// <see cref="CryptoKey"/> (suspend) and to all <c>api_credentials</c>
    /// rows for the tenant (suspend, via the IdentityAccess Contracts seam).
    ///
    /// <para>Returns <c>200 OK</c> with the post-transition
    /// <see cref="TenantResponse"/>. Validation failures surface as 400,
    /// <see cref="ErrorCode.InvariantViolation"/> (already-terminated) as 409,
    /// and <see cref="ErrorCode.NotFound"/> as 404.</para>
    /// </summary>
    /// <param name="id">Tenant identifier from the route.</param>
    /// <param name="request">Inbound JSON DTO (optional reason).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/terminate")]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TerminateAsync(
        [FromRoute] Guid id,
        [FromBody] TerminateTenantRequest? request,
        CancellationToken cancellationToken)
    {
        var command = new TerminateTenantCommand(
            TenantId: new TenantId(id),
            Reason: request?.Reason);

        var result = await _mediator
            .Send(command, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapLifecycleFailure(result);
        }

        // Re-load the aggregate so the response body reflects the post-transition
        // status + child cascades.
        var tenant = await _tenants
            .GetByIdAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return tenant is null
            ? StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Tenant row missing after terminate",
                Detail = "The tenant was successfully terminated but could not be reloaded.",
                Status = StatusCodes.Status500InternalServerError,
            })
            : Ok(BuildTenantResponse(tenant));
    }

    /// <summary>
    /// <c>POST /v1/admin/tenants/{id}/reactivate</c> — move a tenant out of
    /// <see cref="TenantStatus.Suspended"/> back into
    /// <see cref="TenantStatus.Active"/>. Cascades the same transition onto
    /// the suspended <c>api_credentials</c> rows (via the IdentityAccess
    /// Contracts seam) and the suspended <see cref="CryptoKey"/>.
    ///
    /// <para>Returns <c>200 OK</c> with the post-transition
    /// <see cref="TenantResponse"/>. Same error-code → status mapping as
    /// <see cref="SuspendAsync"/>.</para>
    /// </summary>
    /// <param name="id">Tenant identifier from the route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReactivateAsync(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var command = new ReactivateTenantCommand(TenantId: new TenantId(id));

        var result = await _mediator
            .Send(command, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapLifecycleFailure(result);
        }

        var tenant = await _tenants
            .GetByIdAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return tenant is null
            ? StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Tenant row missing after reactivate",
                Detail = "The tenant was successfully reactivated but could not be reloaded.",
                Status = StatusCodes.Status500InternalServerError,
            })
            : Ok(BuildTenantResponse(tenant));
    }

    /// <summary>
    /// <c>POST /v1/admin/tenants/{id}/activate</c> — move a tenant from
    /// <see cref="TenantStatus.Pending"/> into <see cref="TenantStatus.Active"/>.
    /// Does NOT cascade onto the tenant's <c>api_credentials</c> or
    /// <c>crypto_keys</c>: credential and key state is independent of the
    /// tenant's Active flag and is governed by dedicated endpoints.
    ///
    /// <para>Returns <c>200 OK</c> with the post-transition
    /// <see cref="TenantResponse"/>. Validation failures surface as 400,
    /// <see cref="ErrorCode.InvariantViolation"/> (self-transition / terminal
    /// state) as 409, and <see cref="ErrorCode.NotFound"/> (no row with that
    /// id) as 404.</para>
    ///
    /// <para>For the <c>Suspended → Active</c> path that should also reinstate
    /// the suspended signing key and credentials, use the
    /// <c>reactivate</c> endpoint instead.</para>
    /// </summary>
    /// <param name="id">Tenant identifier from the route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ActivateAsync(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var command = new ActivateTenantCommand(TenantId: new TenantId(id));

        var result = await _mediator
            .Send(command, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return MapLifecycleFailure(result);
        }

        // Re-load the aggregate so the response body reflects the post-transition
        // status. (The handler returns only the metadata needed for the audit row.)
        var tenant = await _tenants
            .GetByIdAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return tenant is null
            ? StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Tenant row missing after activate",
                Detail = "The tenant was successfully activated but could not be reloaded.",
                Status = StatusCodes.Status500InternalServerError,
            })
            : Ok(BuildTenantResponse(tenant));
    }

    /// <summary>
    /// Map a lifecycle-handler failure (Suspend / Reactivate / Activate /
    /// Terminate) to the matching HTTP status. Centralized so all four
    /// actions stay one-line call sites. Generic over the success payload so
    /// callers don't have to box through <c>object</c>.
    /// </summary>
    private ActionResult MapLifecycleFailure<T>(Result<T> result) => result.ErrorCode switch
    {
        ErrorCode.ValidationFailed => ValidationProblem(result.ErrorMessage),
        ErrorCode.NotFound => NotFound(new ProblemDetails
        {
            Title = "Tenant not found",
            Detail = result.ErrorMessage,
            Status = StatusCodes.Status404NotFound,
        }),
        ErrorCode.InvariantViolation => Conflict(new ProblemDetails
        {
            Title = "Tenant invariant violated",
            Detail = result.ErrorMessage,
            Status = StatusCodes.Status409Conflict,
        }),
        _ => Problem(
            title: "Tenant lifecycle action failed",
            detail: result.ErrorMessage,
            statusCode: StatusCodes.Status400BadRequest),
    };

    /// <summary>
    /// Project a <see cref="Tenant"/> aggregate into the public
    /// <see cref="TenantResponse"/> shape. Used by the lifecycle endpoints,
    /// which don't carry the request DTO at response time (only the id).
    /// Delegates to <see cref="TenantResponseBuilder.Build"/> so the read-side
    /// handlers project identically.
    /// </summary>
    private static TenantResponse BuildTenantResponse(Tenant tenant) => TenantResponseBuilder.Build(tenant);

    /// <summary>
    /// <c>GET /v1/admin/tenants</c> — read-side paged list with optional filters.
    /// Returns <c>200 OK</c> with a <see cref="PagedTenantResponse"/>: the page
    /// slice plus total count and a <c>hasMore</c> flag.
    ///
    /// <para>Query parameters (all optional):
    /// <list type="bullet">
    ///   <item><c>status</c> — case-insensitive
    ///         <see cref="TenantStatus"/> filter (e.g. <c>"Active"</c>,
    ///         <c>"Pending"</c>). Unknown values surface as 400.</item>
    ///   <item><c>isActive</c> — soft-delete flag filter.</item>
    ///   <item><c>page</c> — 1-based page index. Default <c>1</c>.</item>
    ///   <item><c>pageSize</c> — items per page. Default <c>20</c>, max
    ///         <c>100</c> (validator-enforced).</item>
    /// </list>
    /// </para>
    ///
    /// <para>No state change, no audit row; pure projection endpoint.</para>
    /// </summary>
    /// <param name="request">Inbound query-string DTO bound by ASP.NET Core.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("")]
    [ProducesResponseType(typeof(PagedTenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetListAsync(
        [FromQuery] ListTenantsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Parse the wire-format status string into the enum. Unknown values
        // surface as 400 — better than silently returning an unfiltered list
        // when a client mistypes "Activ" instead of "Active".
        TenantStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<TenantStatus>(request.Status, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return ValidationProblem(
                    $"Unknown status '{request.Status}'. " +
                    $"Allowed: {string.Join(", ", Enum.GetNames<TenantStatus>())}.");
            }

            status = parsed;
        }

        var query = new ListTenantsQuery(
            Status: status,
            IsActive: request.IsActive,
            Page: request.Page,
            PageSize: request.PageSize);

        var result = await _mediator
            .Send(query, cancellationToken)
            .ConfigureAwait(false);

        // Validation failures (out-of-range page / pageSize) surface here too,
        // because the ValidationBehavior runs before the handler.
        if (result.IsFailure)
        {
            return result.ErrorCode switch
            {
                ErrorCode.ValidationFailed => ValidationProblem(result.ErrorMessage),
                _ => Problem(
                    title: "Tenant list failed",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status400BadRequest),
            };
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// <c>GET /v1/admin/tenants/{id:guid}</c> — read-side projection. Returns
    /// <c>200 OK</c> with the <see cref="TenantResponse"/> when a tenant with
    /// that id exists, or <c>404 Not Found</c> otherwise. No state change, no
    /// audit row; this is a pure projection endpoint.
    /// </summary>
    /// <param name="id">Tenant identifier from the route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id:guid}", Name = nameof(GetTenantById))]
    [ProducesResponseType(typeof(TenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTenantById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator
            .Send(new GetTenantByIdQuery(new TenantId(id)), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.ErrorCode switch
            {
                ErrorCode.NotFound => NotFound(new ProblemDetails
                {
                    Title = "Tenant not found",
                    Detail = result.ErrorMessage,
                    Status = StatusCodes.Status404NotFound,
                }),
                _ => Problem(
                    title: "Tenant lookup failed",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status400BadRequest),
            };
        }

        return Ok(result.Value);
    }
}