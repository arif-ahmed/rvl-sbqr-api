using Microsoft.AspNetCore.Http;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Api.Security;

/// <summary>
/// Resolves the audit <c>actor</c> for the Tenancy &amp; Access module from the
/// authenticated principal's <c>sub</c> claim. The claim already carries the
/// actor-tag grammar minted at token time:
/// <list type="bullet">
///   <item><c>platform:{principal}</c> — platform machine principals (the
///         bootstrap admin token used against <c>/admin/**</c>);</item>
///   <item><c>client:{client_id}</c> — tenant FI machine principals;</item>
///   <item><c>user:{id}</c> — human portal users, once Epic 6 ships.</item>
/// </list>
///
/// Lives in the Api layer because the Infrastructure layer must not depend on
/// ASP.NET Core types. <see cref="IHttpContextAccessor"/> is registered globally
/// by the host (<c>AddHttpContextAccessor</c> in <c>SBQR.Api/Program.cs</c>).
/// </summary>
/// <remarks>
/// <para>
/// This provider never throws — a missing context (background job, design-time
/// tooling) or an unauthenticated request falls back to <see cref="SystemActor"/>
/// so the audit interceptor can still stamp rows. The interceptor itself
/// catches exceptions and uses the same fallback, so this is defense in depth.
/// </para>
/// <para>
/// Registered as <c>Scoped</c> in <see cref="TenancyModule.RegisterServices"/> so it
/// shares the lifetime of the inbound HTTP request.
/// </para>
/// </remarks>
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
    /// <returns>
    /// The <c>sub</c> claim of the authenticated principal
    /// (<c>platform:…</c> / <c>client:…</c> / <c>user:…</c>);
    /// <see cref="SystemActor"/> when no principal is present or the claim
    /// is missing.
    /// </returns>
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
