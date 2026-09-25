namespace SBQR.Modules.Tenancy.Domain.Aggregates;

/// <summary>
/// Read-only preconditions the Tenancy <c>Activate</c> /
/// <c>Reactivate</c> handlers enforce before driving the
/// <see cref="Tenant"/> aggregate's state machine.
///
/// <para>
/// Why this exists: the platform asserts that an institute is
/// <see cref="TenantStatus.Active"/> only when the downstream resources
/// that actually let the institute transact exist. An <c>Active</c>
/// tenant with no signing key + no FI credential + no trust-directory
/// row is a state-machine lie — the tenant could not mint verifiable
/// QR codes. The three flags here close that gap.
/// </para>
///
/// <para>
/// Filled by the Tenancy command handlers just before calling
/// <see cref="Tenant.Activate"/>. The aggregate refuses to transition
/// when <see cref="IsReady"/> is <c>false</c>; the handler catches the
/// thrown <see cref="InvalidOperationException"/> and maps it to a
/// <c>409 InvariantViolation</c> carrying the readiness summary.
/// </para>
/// </summary>
/// <param name="HasCredential">
/// At least one <c>ACTIVE</c> row in <c>public.tenant_configurations</c>.
/// Source BC: IdentityAccess. Seam:
/// <c>ITenantConfigurationProvisioner.HasActiveAsync</c>.
/// </param>
/// <param name="HasSigningKey">
/// At least one <c>ACTIVE</c> row in <c>public.crypto_keys</c>.
/// Source BC: KeyCustody. Seam: <c>GetActiveCryptoKeyQuery</c> returning
/// a non-failure result.
/// </param>
/// <param name="HasTrustEntry">
/// An <c>ACTIVE</c> row in <c>public.institution_keys</c>
/// for the tenant's <c>institution_code</c>. Source BC: InstitutionTrust.
/// Seam: <c>GetInstitutionPublicKeyQuery</c> returning a non-null view.
/// (Null is the negative answer — the query returns the row when it
/// exists, and the validator already filters to <c>status = ACTIVE</c>.)
/// </param>
public sealed record TenantReadiness(
    bool HasCredential,
    bool HasSigningKey,
    bool HasTrustEntry)
{
    /// <summary>True iff every precondition holds.</summary>
    public bool IsReady => HasCredential && HasSigningKey && HasTrustEntry;

    /// <summary>
    /// Convenience sentinel for the <c>Activate(actor)</c> overload used
    /// by domain unit tests that drive the aggregate directly. Forces
    /// <see cref="IsReady"/> to <c>true</c> so the gate never fires in
    /// test paths that aren't exercising the readiness check.
    /// Production command handlers must construct their own
    /// <see cref="TenantReadiness"/> from the three real seam reads.
    /// </summary>
    public static TenantReadiness AllReady { get; } =
        new(HasCredential: true, HasSigningKey: true, HasTrustEntry: true);

    /// <summary>
    /// Compact string form ("credential=true, signing_key=false,
    /// trust_entry=true") used in the aggregate's
    /// <see cref="InvalidOperationException"/> message — the message rides
    /// through the handler's <c>InvariantViolation</c> result and shows up
    /// in the controller's <c>409</c> body, giving the operator the
    /// specific missing precondition.
    /// </summary>
    public override string ToString() =>
        $"credential={HasCredential}, signing_key={HasSigningKey}, trust_entry={HasTrustEntry}";
}
