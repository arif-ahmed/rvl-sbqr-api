namespace SBQR.SharedKernel.Cryptography;

/// <summary>
/// Durable, cross-platform storage for wrapped per-tenant signing-key
/// material — the Phase-1 software vault. This port sits in the shared
/// kernel (like <see cref="ISigningProvider"/>) because two bounded
/// contexts share one physical store:
///
/// <list type="bullet">
///   <item><b>Tenancy</b> writes at institute registration (its
///         <c>ILocalKeyVault</c> adapter delegates here);</item>
///   <item><b>KeyCustody</b> reads at signing time to unwrap the tenant's
///         active private key in memory.</item>
/// </list>
///
/// The concrete implementation lives inside KeyCustody's cryptographic
/// boundary (<c>Infrastructure/Custody/</c>): AES-256-GCM blobs under an
/// operator-supplied KEK. Plaintext private bytes never leave the
/// implementation's memory except through <see cref="TryLoad"/>.
/// </summary>
public interface ISigningKeyStore
{
    /// <summary>
    /// Wrap and persist <paramref name="privateKeyBytes"/> under
    /// <paramref name="suggestedHandle"/>. Returns the opaque handle the
    /// caller persists in <c>crypto_keys.custody_key_reference</c>.
    /// </summary>
    /// <param name="tenantId">Owning tenant (namespaces the handle).</param>
    /// <param name="privateKeyBytes">Private-key bytes in the clear — wrapping is the store's job.</param>
    /// <param name="suggestedHandle">Caller-supplied handle; the store binds it cryptographically (AAD).</param>
    string Store(Guid tenantId, ReadOnlySpan<byte> privateKeyBytes, string suggestedHandle);

    /// <summary>
    /// Best-effort unwrap of the material stored under
    /// <paramref name="custodyHandle"/>. Returns <c>null</c> when the
    /// handle is unknown; throws <see cref="System.Security.Cryptography.CryptographicException"/>
    /// when the stored blob fails authentication (wrong KEK or tampering) —
    /// fail-closed, never partial plaintext.
    /// </summary>
    byte[]? TryLoad(string custodyHandle);
}
