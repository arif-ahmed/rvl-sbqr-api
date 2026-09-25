namespace SBQR.Modules.Tenancy.Domain.Aggregates;

/// <summary>
/// Strongly-typed identifier for a <see cref="Tenant"/>. Wraps a <see cref="Guid"/>
/// in a zero-alloc <c>readonly record struct</c> so the type system can distinguish a
/// tenant id from any other <see cref="Guid"/> in the codebase. Server-assigned at
/// <see cref="Tenant.Register"/> time — never accepted from inbound payloads.
/// </summary>
public readonly record struct TenantId(Guid Value);
