using MediatR;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.IdentityAccess.Application.Commands.IssueToken;

/// <summary>
/// MediatR command for <c>POST /v1/oauth/token</c> (OAuth 2.1 client-credentials
/// grant). Two credential sources resolve to two different claim sets:
/// <list type="number">
///   <item><b>Platform bootstrap client</b> (<c>Auth:Bootstrap</c> config) —
///         issues an <c>admin</c>-scoped token with
///         <c>sub=platform:bootstrap-admin</c> and no tenant binding. This is
///         the only credential that can ever mint <c>scope=admin</c>.</item>
///   <item><b>Tenant FI credential</b> (<c>api_credentials</c> row) — issues
///         a tenant-scoped token (<c>sub=client:{client_id}</c>,
///         <c>tenant_id</c> claim, QR operation scopes). Never carries
///         <c>admin</c>.</item>
/// </list>
///
/// Failure values use the OAuth2 error codes verbatim as
/// <see cref="Result{T}.ErrorMessage"/> (<c>invalid_client</c>,
/// <c>unsupported_grant_type</c>) so the controller can map them to
/// RFC 6749 §5.2 responses without string-sniffing.
/// </summary>
public sealed record IssueClientCredentialsTokenCommand(
    string GrantType,
    string ClientId,
    string ClientSecret,
    string? PackageId = null) : IRequest<Result<ClientCredentialsTokenResult>>;

/// <summary>
/// Success payload — the OAuth2 token endpoint response body.
/// </summary>
/// <param name="AccessToken">The encoded, signed JWT.</param>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="ExpiresIn">Lifetime in seconds.</param>
public sealed record ClientCredentialsTokenResult(
    string AccessToken,
    string TokenType,
    int ExpiresIn);
