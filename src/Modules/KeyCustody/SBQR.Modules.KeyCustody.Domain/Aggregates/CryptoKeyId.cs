namespace SBQR.Modules.KeyCustody.Domain.Aggregates;

/// <summary>
/// Strongly-typed identifier for a <see cref="CryptoKey"/>. Wraps a
/// <see cref="Guid"/> in a zero-alloc <c>readonly record struct</c> so the type
/// system can distinguish a crypto-key id from any other <see cref="Guid"/> in
/// the codebase. Server-assigned at <see cref="CryptoKey.Generate"/> or
/// <see cref="CryptoKey.Adopt"/> time — never accepted from inbound payloads.
/// </summary>
public readonly record struct CryptoKeyId(Guid Value);
