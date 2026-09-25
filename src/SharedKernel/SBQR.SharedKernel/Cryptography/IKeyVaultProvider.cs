namespace SBQR.SharedKernel.Cryptography;

/// <summary>
/// Backend selector for the tenant signing-key vault. One physical store
/// (the Phase-1 software vault under <c>KeyCustody:KeyStoreDirectory</c>)
/// backs <see cref="ISigningKeyStore"/> today; this port exists so a future
/// Azure Key Vault / AWS KMS / HSM backend can be swapped in without
/// touching any consumer of <see cref="ISigningKeyStore"/>.
///
/// <para>
/// Implementations return their own <see cref="ISigningKeyStore"/> via
/// <see cref="GetSigningKeyStore"/>. They are responsible for honouring
/// whatever backend-specific configuration their backend needs (KEK, KMS
/// key id, HSM partition label, …).
/// </para>
///
/// <para>
/// <see cref="Metadata"/> is for <i>identification</i> only — backend name,
/// cipher, KEK <i>source</i> (a config key, never the KEK bytes). It must
/// never contain private-key material, KEK material, or any secret.
/// </para>
///
/// <para>
/// Selection is config-driven (today: <c>Crypto:VaultProvider</c>). The
/// host refuses to start when the configured provider name does not match
/// any registered implementation — see <c>SBQR.Api/Program.cs</c>
/// §10a (production guard).
/// </para>
/// </summary>
public interface IKeyVaultProvider
{
    /// <summary>
    /// Stable identifier of the backend (e.g. <c>"Local"</c>, <c>"AzureKeyVault"</c>).
    /// Used in configuration, in audit rows, and to match the
    /// <c>Crypto:VaultProvider</c> switch in DI.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Backend identity surfaced for audit rows. Keys only — values must
    /// not contain KEK material, private-key material, or any secret. The
    /// KEK <i>source path</i> (e.g. <c>"config:KeyCustody:VaultKek"</c>) is
    /// fine; the KEK itself is not.
    /// </summary>
    IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Build / return the backend's <see cref="ISigningKeyStore"/> instance.
    /// Implementations are expected to construct lazily (the store may
    /// touch the filesystem / network on first use) and to be safe to call
    /// multiple times — the DI graph registers the returned instance as a
    /// singleton.
    /// </summary>
    ISigningKeyStore GetSigningKeyStore();
}
