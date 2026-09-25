using System.Text.RegularExpressions;

namespace SBQR.Modules.InstitutionTrust.Application.Services;

/// <summary>
/// The single definition of "what a well-formed institution record looks
/// like". Referenced by BOTH lines of defence: the FluentValidation
/// <c>UpsertInstitutionValidator</c> (manual admin path, produces a clean
/// <c>Result.Failure</c>) and <see cref="InstitutionUpsertService"/>'s own
/// internal guard (the only protection the trust-sync path has, since the
/// sync job calls the service directly and never goes through MediatR).
/// </summary>
public static partial class TrustRecordPatterns
{
    /// <summary>Institution code: exactly six digits (Tag 26 sub 01 ‖ sub 02).</summary>
    [GeneratedRegex(@"^[0-9]{6}$")]
    public static partial Regex InstitutionCode();

    /// <summary>Institution type: exactly two digits (Tag 26 sub 01 — the InstitutionId prefix).</summary>
    [GeneratedRegex(@"^[0-9]{2}$")]
    public static partial Regex InstituteType();

    /// <summary>A PEM block — SPKI Ed25519 '-----BEGIN PUBLIC KEY-----'.</summary>
    [GeneratedRegex(@"-----BEGIN [A-Z ]+-----[\s\S]+-----END [A-Z ]+-----")]
    public static partial Regex Pem();

    /// <summary>Matches the <c>institution_name VARCHAR(200)</c> column width.</summary>
    public const int MaxInstitutionNameLength = 200;
}
