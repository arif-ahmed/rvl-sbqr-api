using Microsoft.AspNetCore.Http;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.IdentityAccess.Api.Security;

/// <summary>
/// Resolves the audit <c>actor</c> for the Identity &amp; Access module from the
/// authenticated principal's <c>sub</c> claim. The claim carries the actor-tag
/// grammar minted at token time by this module's token endpoint
/// (<c>POST /v1/oauth/token</c>):
/// <list type="bullet">
///   <item><c>platform:{principal}</c> — platform machine principals (the bootstrap
///         admin token used against <c>/admin/**</c>);</item>
///   <item><c>client:{client_id}</c> — tenant FI machine principals;</item>
///   <item><c>user:{id}</c> — human portal users, once Epic 6 ships.</item>
/// </list>
///
/// Lives in the Api layer because the Infrastructure layer must not depend on
/// ASP.NET Core types. <see cref="IHttpContextAccessor"/> is registered globally
/// by the host (<c>AddHttpContextAccessor</c> in <c>SBQR.Api/Program.cs</c>).
///
/// <para>This is the module-local implementation of the shared-kernel
/// <see cref="IActorProvider"/> contract; the Tenancy module keeps its own copy.
/// A future epic may hoist a single shared implementation into the Audit module.
/// </para>
/// </summary>
public sealed class HttpContextActorProvider : IActorProvider
{
    /// <summary>
    /// Logical actor name recorded when no authenticated principal is
    /// available (background work, design-time tooling, anonymous requests,
    /// fallback).
    /// </summary>
    public const string SystemActor = "system";

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Construct the provider with an <see cref="IHttpContextAccessor"/> resolved
    /// from DI (<c>AddHttpContextAccessor()</c> is registered in <c>SBQR.Api</c>).
    /// </summary>
    /// <param name="httpContextAccessor">Accessor for the current HTTP request.</param>
    public HttpContextActorProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    /// <inheritdoc/>
    public string CurrentActor()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
        {
            return SystemActor;
        }

        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            // JwtBearer runs with MapInboundClaims=false, so the short claim
            // name "sub" survives inbound mapping.
            var subject = user.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(subject))
            {
                return subject;
            }
        }

        return SystemActor;
    }
}
