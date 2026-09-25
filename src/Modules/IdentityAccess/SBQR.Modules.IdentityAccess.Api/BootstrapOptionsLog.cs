using Microsoft.Extensions.Logging;

namespace SBQR.Modules.IdentityAccess.Api;

/// <summary>
/// LoggerMessage source-generator sink for soft-validation warnings emitted
/// by the platform-bootstrap <c>PlatformBootstrapOptions</c> configuration
/// step. Source-generated to satisfy CA1848 (zero-allocation logging on the
/// DI-resolve hot path) — see IdentityAccessModule.cs §6.
///
/// <para>Why this lives in its own file: <see cref="System.Text.RegularExpressions.GeneratedRegexAttribute"/>
/// and friends must be the only members of a partial type, and the source
/// generator prefers a dedicated file. Keeping the partial here also keeps
/// <c>IdentityAccessModule.cs</c> focused on composition.</para>
/// </summary>
internal static partial class BootstrapOptionsLog
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Auth:Bootstrap:ClientSecretHash is unset. Bootstrap token issuance will fail closed (401 invalid_client). Generate with `dotnet run -- --generate-bootstrap-secret`; in development store with `dotnet user-secrets set Auth:Bootstrap:ClientSecretHash \"<phc>\"`; in Staging/RC/Production set Auth__Bootstrap__ClientSecretHash from Key Vault.")]
    public static partial void ClientSecretHashUnset(ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Auth:Bootstrap:ClientSecretHash is not an Argon2id PHC string (missing $argon2id$ prefix). Bootstrap token issuance will fail closed. Regenerate with `dotnet run -- --generate-bootstrap-secret`.")]
    public static partial void ClientSecretHashNotArgon2id(ILogger logger);
}
