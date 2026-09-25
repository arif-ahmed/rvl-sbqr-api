using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Commands.ProvisionTenantConfiguration;

/// <summary>
/// MediatR command for <c>POST /v1/admin/tenants/{id}/tenant-configuration</c>. Carries
/// only the target <see cref="TenantId"/> — the institution code that shapes
/// the <c>client_id</c> (<c>{code}-{8hex}</c>) is read from the tenant row by
/// the handler, so a caller cannot smuggle in a mismatched code.
///
/// <para>The handler is responsible for:</para>
/// <list type="number">
///   <item>Loading the tenant and rejecting Suspended / Terminated tenants —
///         a configuration minted for a suspended tenant could never obtain a
///         token (the token endpoint re-checks admission state), so
///         provisioning one is a confusing no-op.</item>
///   <item>Delegating to
///         <see cref="IdentityAccess.Contracts.ITenantConfigurationProvisioner.ProvisionAsync"/>
///         (IdentityAccess owns the <c>tenant_configurations</c> aggregate,
///         the Argon2id hash, and the per-configuration audit row).</item>
///   <item>Writing one Tenancy-side audit breadcrumb so the identity of the
///         acting admin survives — the IdentityAccess row records
///         <c>system</c> as the actor because the seam is process-internal.</item>
/// </list>
///
/// <para>This is the dedicated configuration endpoint the register flow defers
/// to: <c>POST /v1/admin/tenants</c> ships the tenant row without configurations,
/// and this command provisions the initial FI client configuration on demand.
/// Configuration rotation is a separate future flow; re-provisioning while an
/// active configuration exists fails with
/// <see cref="SharedKernel.Application.ErrorCode.InvariantViolation"/>
/// (→ HTTP 409).</para>
///
/// <para>
/// The admin's QR capability choice (<paramref name="isQrGenerationAllowed"/>
/// / <paramref name="isQrValidationAllowed"/>) is captured here from the
/// request body and stamped onto the new <c>tenant_configurations</c> row.
/// Both default to <c>true</c>; the validator enforces "at least one must be
/// true" so a credential whose row authorises zero QR flows can never be
/// minted.
/// </para>
/// </summary>
/// <param name="TenantId">Target tenant id (from the route).</param>
/// <param name="IsQrGenerationAllowed">Initial QR-QrGeneration capability for
///     the new <c>tenant_configurations</c> row. Defaults to <c>true</c>.</param>
/// <param name="IsQrValidationAllowed">Initial QR-Verification capability for
///     the new <c>tenant_configurations</c> row. Defaults to <c>true</c>.</param>
public sealed record ProvisionTenantConfigurationCommand(
    TenantId TenantId,
    bool IsQrGenerationAllowed = true,
    bool IsQrValidationAllowed = true) : IRequest<Result<ProvisionTenantConfigurationResult>>;
