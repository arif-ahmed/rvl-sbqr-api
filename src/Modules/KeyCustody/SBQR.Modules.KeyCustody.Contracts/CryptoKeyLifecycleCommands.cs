using MediatR;

namespace SBQR.Modules.KeyCustody.Contracts;

/// <summary>
/// Cross-module cascade command: suspend the tenant's currently-ACTIVE
/// signing key, if one exists. Fired by Tenancy's
/// <c>SuspendTenantCommandHandler</c> / <c>TerminateTenantCommandHandler</c>
/// when the owning tenant moves into Suspended/Terminated. A no-op (not an
/// error) when the tenant has no active key — Tenancy's register flow may
/// race ahead of a key being minted.
/// </summary>
public sealed record SuspendTenantSigningKeysCommand(Guid TenantId) : IRequest;

/// <summary>
/// Cross-module cascade command: reinstate the tenant's currently-SUSPENDED
/// signing key, if one exists. Fired by Tenancy's
/// <c>ReactivateTenantCommandHandler</c> when the owning tenant moves out of
/// Suspended. A no-op (not an error) when the tenant has no suspended key.
/// </summary>
public sealed record ReinstateTenantSigningKeysCommand(Guid TenantId) : IRequest;
