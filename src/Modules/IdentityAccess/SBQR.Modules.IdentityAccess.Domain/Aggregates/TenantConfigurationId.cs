namespace SBQR.Modules.IdentityAccess.Domain.Aggregates;

/// <summary>
/// Strongly-typed identifier for a <see cref="TenantConfiguration"/>. Wraps a
/// <see cref="Guid"/> in a zero-alloc <c>readonly record struct</c> so the type
/// system can distinguish a configuration id from any other <see cref="Guid"/> in
/// the codebase. Server-assigned at <see cref="TenantConfiguration.Register"/>
/// time — never accepted from inbound payloads.
/// </summary>
public readonly record struct TenantConfigurationId(Guid Value);
