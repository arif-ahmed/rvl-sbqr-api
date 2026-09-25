namespace SBQR.SharedKernel.Application;

/// <summary>
/// Resolves the current request's acting principal as a string suitable for the
/// <c>created_by</c> / <c>modified_by</c> audit columns. Implementations live in
/// each module's Api layer (where <c>HttpContext</c> is reachable); the contract
/// itself lives in the shared kernel so every module's audit interceptor and
/// command handlers share one actor grammar.
///
/// Values follow the actor-tag grammar minted into the <c>sub</c> claim at
/// token time by the IdentityAccess module's token endpoint:
/// <c>platform:{principal}</c> (platform machine principals / bootstrap admin),
/// <c>client:{client_id}</c> (tenant FI machine principals),
/// <c>user:{id}</c> (human portal users, Epic 6). The <c>"system"</c> fallback
/// covers background jobs and boot-time work.
///
/// Promoted from Tenancy when the OAuth2 client-credentials surface moved to
/// the IdentityAccess bounded context — both modules' audit interceptors and
/// the register/suspend cascades need the same seam.
/// </summary>
public interface IActorProvider
{
    /// <summary>
    /// The current actor identifier. Must never be null; the audit interceptor
    /// will fall back to <c>"system"</c> if the resolver itself fails, but
    /// callers should provide a stable value.
    /// </summary>
    string CurrentActor();
}
