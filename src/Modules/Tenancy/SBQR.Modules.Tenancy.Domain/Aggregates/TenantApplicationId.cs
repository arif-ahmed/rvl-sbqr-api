namespace SBQR.Modules.Tenancy.Domain.Aggregates;

/// <summary>
/// Strongly-typed identifier for a <see cref="TenantApplication"/>. Wraps a
/// <see cref="Guid"/> in a zero-alloc <c>readonly record struct</c> so the
/// type system can distinguish a tenant-application id from any other
/// <see cref="Guid"/> in the codebase. Server-assigned at
/// <see cref="TenantApplication.Register"/> time — never accepted from inbound
/// payloads.
/// </summary>
public readonly record struct TenantApplicationId(Guid Value);
