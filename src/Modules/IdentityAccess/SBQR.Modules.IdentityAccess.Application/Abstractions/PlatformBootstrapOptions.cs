namespace SBQR.Modules.IdentityAccess.Application.Abstractions;

/// <summary>
/// Configuration for the platform bootstrap client — "client zero" of the
/// OAuth 2.1 client-credentials flow. The platform itself (an operator or an
/// ops pipeline) authenticates with this credential to obtain the
/// <c>admin</c>-scoped token required by <c>POST /v1/admin/tenants</c>, which
/// then mints each FI's own tenant-scoped credential. Bound from the
/// <c>Auth:Bootstrap</c> configuration section by the IdentityAccess module
/// composition root.
///
/// Storage model — the Argon2id hash form is the ONLY acceptable shape in
/// EVERY environment (Local / Development / Staging / RC / Production):
/// <list type="bullet">
///   <item><b>ClientSecretHash</b> (Argon2id PHC string —
///         <c>$argon2id$v=19$m=...,t=...,p=...$&lt;salt&gt;$&lt;digest&gt;</c>)
///         is the required form across all environments. Generated once via
///         <c>dotnet run -- --generate-bootstrap-secret</c>; the plaintext is
///         printed exactly once and never stored. Source: user-secrets in
///         development; env var <c>Auth__Bootstrap__ClientSecretHash</c>
///         (sourced from Key Vault) in non-development.</item>
/// </list>
/// </summary>
public sealed class PlatformBootstrapOptions
{
    /// <summary>
    /// The <c>client_id</c> of the bootstrap client. Must be distinct from
    /// every tenant client id (tenant ids are <c>{code}-{8hex}</c>, so a
    /// hyphenated word can never collide).
    /// </summary>
    public string ClientId { get; init; } = "platform-bootstrap";

    /// <summary>
    /// Argon2id PHC-format hash of the bootstrap secret
    /// (<c>$argon2id$v=19$m=…$…</c>). Required form across every
    /// environment; the plaintext secret is never available to the process.
    /// </summary>
    public string? ClientSecretHash { get; init; }

    /// <summary>
    /// The scope claim granted to the bootstrap token. Must match the
    /// <c>admin-credential-tree</c> policy's
    /// <c>RequireClaim("scope", "admin")</c> — do not change one without
    /// the other.
    /// </summary>
    public string AdminScope { get; init; } = "admin";

    /// <summary>
    /// The <c>sub</c> claim (and audit actor) stamped on bootstrap tokens.
    /// </summary>
    public string Subject { get; init; } = "platform:bootstrap-admin";

    /// <summary>True when the Argon2id hash is configured.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(ClientSecretHash);
}
