using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SBQR.Modules.IdentityAccess.Api.Contracts;
using SBQR.Modules.IdentityAccess.Application.Commands.IssueToken;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.IdentityAccess.Api.Controllers;

/// <summary>
/// <c>POST /v1/oauth/token</c> — the OAuth 2.1 client-credentials token endpoint
/// (RFC 6749 §3.2, §10.2 client-credentials grant). Anonymous by definition:
/// the <c>client_secret</c> IS the authentication. Brute force is blunted by
/// the <see cref="RateLimitPolicyNames.TokenEndpoint"/> fixed-window limiter
/// (per remote IP) applied on this controller.
///
/// <para>Two credential populations resolve here: the platform bootstrap
/// client (config-held; receives <c>scope=admin</c>) and tenant FI
/// credentials (<c>public.tenant_configurations</c>; receive tenant-scoped
/// tokens). Every issuance writes an <c>auth.token.issued</c> audit row.</para>
///
/// <para>Flows into the <c>v1.public</c> OpenAPI document (no
/// <c>ApiExplorerSettings</c> annotation) — every caller class needs this
/// endpoint, unlike the <c>internal</c>-documented admin surface it unlocks.
/// See
/// <c>docs/superpowers/specs/2026-09-04-public-vs-internal-api-docs-design.md</c>
/// for the full public/internal document split.</para>
///
/// Moved from the Tenancy module to IdentityAccess along with the
/// <c>tenant_configurations</c> aggregate and the token-minting seams.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/oauth/token")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicyNames.TokenEndpoint)]
[Produces("application/json")]
public sealed class OAuthController : ControllerBase
{
    private readonly ISender _mediator;

    public OAuthController(ISender mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    // RFC 6749 §5.2 error codes. The handler emits InvalidClient and
    // UnsupportedGrantType as ErrorMessage verbatim; everything else maps
    // to InvalidRequest.
    private const string InvalidClient = "invalid_client";
    private const string UnsupportedGrantType = "unsupported_grant_type";
    private const string InvalidRequest = "invalid_request";

    /// <summary>
    /// Exchange <c>client_id</c> + <c>client_secret</c> for a bearer access
    /// token. Accepts <c>application/x-www-form-urlencoded</c> (spec) or JSON.
    /// Success: <c>200</c> with <c>access_token</c> /
    /// <c>token_type: Bearer</c> / <c>expires_in</c> (seconds).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded", "application/json")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OAuthErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(OAuthErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TokenAsync(CancellationToken cancellationToken)
    {
        // RFC 6749 §2.3.1 sends credentials as form encoding; JSON is a
        // developer-convenience acceptance. Missing body → invalid_request.
        TokenRequest? request;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
            request = new TokenRequest
            {
                GrantType = form["grant_type"].ToString(),
                ClientId = form["client_id"].ToString(),
                ClientSecret = form["client_secret"].ToString(),
                // FR-AUTH-002: optional mobile-app allow-list field. Empty
                // when the caller is a tenant backend (which has no package
                // to send); populated when the caller is a mobile app
                // talking directly to the platform.
                PackageId = form["package_id"].ToString(),
            };
        }
        else
        {
            request = await Request
                .ReadFromJsonAsync<TokenRequest>(cancellationToken)
                .ConfigureAwait(false);
        }

        if (request is null)
        {
            return OAuthError(StatusCodes.Status400BadRequest, "invalid_request");
        }

        var command = new IssueClientCredentialsTokenCommand(
            GrantType: request.GrantType,
            ClientId: request.ClientId,
            ClientSecret: request.ClientSecret,
            PackageId: request.PackageId);

        var result = await _mediator.Send(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            // RFC 6749 §5.2: only `invalid_request`, `invalid_client`,
            // and `unsupported_grant_type` are spec-permitted here. The
            // handler signals these via ErrorMessage verbatim, plus the
            // ValidationFailed → invalid_request case for pipeline-level
            // (empty-field) rejections.
            var (status, code) = result switch
            {
                { ErrorMessage: InvalidClient } => (StatusCodes.Status401Unauthorized, InvalidClient),
                { ErrorMessage: UnsupportedGrantType } => (StatusCodes.Status400BadRequest, UnsupportedGrantType),
                { ErrorCode: ErrorCode.ValidationFailed } => (StatusCodes.Status400BadRequest, InvalidRequest),
                _ => (StatusCodes.Status400BadRequest, InvalidRequest),
            };
            return OAuthError(status, code);
        }

        var issued = result.Value;
        return Ok(new TokenResponse(
            AccessToken: issued.AccessToken,
            TokenType: issued.TokenType,
            ExpiresIn: issued.ExpiresIn));
    }

    private ObjectResult OAuthError(int statusCode, string error) =>
        StatusCode(statusCode, new OAuthErrorResponse(Error: error));

    /// <summary>OAuth2 success body (RFC 6749 §5.1).</summary>
    public sealed record TokenResponse(
        string AccessToken,
        string TokenType,
        int ExpiresIn);

    /// <summary>OAuth2 failure body (RFC 6749 §5.2) — camelCase per spec examples.</summary>
    public sealed record OAuthErrorResponse(string Error);
}
