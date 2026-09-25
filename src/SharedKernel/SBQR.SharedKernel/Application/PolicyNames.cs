namespace SBQR.SharedKernel.Application;

/// <summary>
/// Authorization policy names. The IdentityAccess module registers the
/// concrete policy implementations via <c>AddAuthorization</c>; every
/// other module refers to these constants in
/// <c>[Authorize(Policy = PolicyNames.X)]</c> attributes so that no
/// module takes a hard dependency on IdentityAccess internals.
///
/// This is the Open Host Service relationship from tactical-design.md §5.
/// </summary>
public static class PolicyNames
{
    /// <summary>
    /// Mobile-JWT bearer scheme — used by the public-facing
    /// <c>public.openapi.json</c> document (the QR generate/validate
    /// endpoints).
    /// </summary>
    public const string MobileJwt = "mobile-jwt";

    /// <summary>
    /// Admin credential-tree scheme — used by the
    /// <c>internal-admin.openapi.json</c> document (key management,
    /// audit, tenant lifecycle, etc.).
    /// </summary>
    public const string AdminCredentialTree = "admin-credential-tree";

    /// <summary>
    /// Required for endpoints that manage signing-key lifecycle
    /// (activate, retire, revoke). Sits on top of
    /// <see cref="AdminCredentialTree"/>.
    /// </summary>
    public const string KeyAdmin = "key-admin";

    /// <summary>
    /// Required for endpoints that trigger private-key signing
    /// (e.g. <c>POST /api/v1/cert/sign</c>).
    /// </summary>
    public const string Signer = "signer";

    /// <summary>
    /// Tenant-facing QR generation (<c>POST /v1/qr/generate/static</c> and
    /// <c>POST /v1/qr/generate/dynamic</c>). Held by every tenant FI client
    /// credential (scope <c>qr:generate</c> minted at token time). Sep-17
    /// scope: server-to-server plane only — the mobile plane (blockers B1/B2)
    /// lands post-deadline.
    /// </summary>
    public const string QrGenerate = "qr-generate";

    /// <summary>
    /// Tenant-facing QR verification (<c>POST /v1/qr/validate</c>). Scope
    /// <c>qr:validate</c>; same server-to-server-only note as
    /// <see cref="QrGenerate"/>.
    /// </summary>
    public const string QrValidate = "qr-validate";
}
