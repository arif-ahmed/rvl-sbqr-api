namespace SBQR.Modules.Tenancy.Contracts;

/// <summary>
/// Cross-module read seam for resolving a tenant's public identity — the
/// attributes a QR payload must carry (institution code) and its admission
/// state. Consumed by QrGeneration (to stamp Tag 26 from the tenant's real
/// institution identity, never caller-supplied) and by Verification (to
/// decide own-custody vs trust-directory key resolution).
///
/// Implementation: <c>TenantDirectory</c> in
/// <c>SBQR.Modules.Tenancy.Infrastructure</c>, registered by
/// <c>TenancyModule.RegisterServices</c>.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>
    /// Resolve the tenant's public identity, or <c>null</c> when no such
    /// tenant exists. <see cref="TenantPublicInfo.InstitutionCode"/> is the
    /// six-digit code shared with the BB trust directory (Tag 26 sub 01 ‖ sub 02).
    /// </summary>
    Task<TenantPublicInfo?> LookupAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverse lookup by institution code — how Verification decides whether
    /// a scanned QR's issuer is one of our own tenants (own-custody key
    /// resolution) or an external institution (trust-directory resolution).
    /// Returns the active tenant holding the code, or <c>null</c>.
    /// </summary>
    Task<TenantPublicInfo?> LookupByInstitutionCodeAsync(
        string institutionCode,
        CancellationToken cancellationToken = default);
}

/// <summary>The public identity of one tenant, as other modules may see it.</summary>
/// <param name="TenantId">Opaque tenant id.</param>
/// <param name="InstitutionCode">BB-assigned six-digit institution code (Tag 26 sub 01 ‖ sub 02).</param>
/// <param name="InstitutionName">Display name (institution name registered at onboarding).
/// Exposed here so cross-module write paths (e.g. KeyCustody auto-publishing the public key
/// into the InstitutionTrust trust directory on crypto-create) can reuse the same directory
/// seam instead of taking a second dependency on the Tenancy aggregate.</param>
/// <param name="Admission">Current admission state, mapped from the internal
/// <c>TenantStatus</c> aggregate enum.</param>
public sealed record TenantPublicInfo(
    Guid TenantId,
    string InstitutionCode,
    string InstitutionName,
    TenantAdmissionState Admission);