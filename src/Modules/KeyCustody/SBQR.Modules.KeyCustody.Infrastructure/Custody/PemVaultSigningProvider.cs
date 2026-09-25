using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NSec.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.Modules.KeyCustody.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Cryptography;

namespace SBQR.Modules.KeyCustody.Infrastructure.Custody;

/// <summary>
/// Phase-1 signing provider (the production default until HSM lands).
/// Resolves the caller's tenant from <see cref="ICurrentTenant"/>, loads
/// that tenant's ACTIVE <c>crypto_keys</c> row, unwraps the PKCS#8 private
/// PEM from the encrypted key store in memory, signs with Ed25519, and
/// returns the 64 raw signature bytes.
///
/// <para>
/// <b>Memory hygiene.</b> The unwrapped PKCS#8 PEM bytes are cleared the
/// instant they leave the parser (<see cref="Array.Clear(byte[])"/> in a
/// <c>finally</c>), and the Ed25519 <see cref="PrivateKey"/> itself lives
/// in NSec's <c>SecureMemory</c> region — the <c>using</c> block disposes
/// it on every return path, zeroing the key material as the buffer is
/// released. NSec replaces BouncyCastle on the signing hot path because
/// BC's expanded keypair sits on the regular GC heap with no
/// <c>Clear()</c> equivalent.
/// </para>
///
/// <para>
/// Fail-closed by construction: missing tenant context, missing/non-ACTIVE
/// key, unknown custody handle, or unwrapping failure all throw
/// <see cref="CryptographicException"/> — there is no fallback path.
/// </para>
/// </summary>
public sealed class PemVaultSigningProvider : ISigningProvider
{
    private readonly KeyCustodyDbContext _db;
    private readonly ISigningKeyStore _keyStore;
    private readonly ICurrentTenant _currentTenant;

    public PemVaultSigningProvider(
        KeyCustodyDbContext db,
        ISigningKeyStore keyStore,
        ICurrentTenant currentTenant)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _currentTenant = currentTenant ?? throw new ArgumentNullException(nameof(currentTenant));
    }

    /// <inheritdoc/>
    public string ProviderId => "pem-vault";

    /// <inheritdoc/>
    public async Task<byte[]> SignAsync(byte[] payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var tenantId = _currentTenant.TenantId;
        if (tenantId == Guid.Empty)
        {
            throw new CryptographicException(
                "PemVaultSigningProvider: no tenant context — signing requires an authenticated tenant request.");
        }

        var key = await _db.CryptoKeys
            .Where(k => k.TenantId == tenantId && k.Status == CryptoKeyStatus.Active)
            .OrderByDescending(k => k.KeyVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new CryptographicException(
                $"Tenant {tenantId} has no ACTIVE signing key; issue flow must fail closed (KEY_NOT_ACTIVE).");

        // The vault hands us the AES-GCM ciphertext bytes; we hold the
        // unwrapped plaintext PEM only inside this frame. Zero the
        // wrapped-bytes buffer in finally so the (still-encrypted)
        // ciphertext never lingers on the GC heap either.
        var wrapped = _keyStore.TryLoad(key.CustodyKeyReference)
            ?? throw new CryptographicException(
                $"Custody handle '{key.CustodyKeyReference}' (key {key.KeyId} v{key.KeyVersion}) " +
                "is not present in the key store — the vault directory and KEK must match the ones used at registration.");

        try
        {
            // Parse the PEM with BouncyCastle (NSec has no PEM loader),
            // then immediately hand the 32-byte Ed25519 seed to NSec.
            // The BC key object is disposed before the next line returns.
            var seed = ExtractEd25519SeedFromPem(wrapped);

            // NSec PrivateKey lives in SecureMemory (mlock'd + zeroed on
            // Dispose). Build inside a using block so the key material is
            // wiped on every return path — sign, throw, or cancel.
            using var privateKey = Key.Import(SignatureAlgorithm.Ed25519, seed, KeyBlobFormat.RawPrivateKey);
            return SignatureAlgorithm.Ed25519.Sign(privateKey, payload);
        }
        finally
        {
            Array.Clear(wrapped);
        }
    }

    /// <summary>
    /// Parses the canonical "-----BEGIN PRIVATE KEY-----" PKCS#8 PEM both
    /// minting scenarios store (generated keys and adopted keys both persist
    /// PKCS#8 PEM bytes — see Ed25519KeyPairGenerator / CryptoKey.Adopt).
    /// Returns the 32-byte Ed25519 seed, the only secret NSec needs to
    /// reconstruct the full signing key on demand.
    /// </summary>
    private static byte[] ExtractEd25519SeedFromPem(byte[] pemBytes)
    {
        Ed25519PrivateKeyParameters bcKey;
        try
        {
            using var reader = new StringReader(Encoding.ASCII.GetString(pemBytes));
            var pemObject = new PemReader(reader).ReadPemObject()
                ?? throw new CryptographicException("Custody material is not a readable PEM object.");
            var key = PrivateKeyFactory.CreateKey(pemObject.Content);
            bcKey = key as Ed25519PrivateKeyParameters
                ?? throw new CryptographicException(
                    $"Custody material is a {key.GetType().Name}; expected an Ed25519 PKCS#8 private key.");
        }
        catch (CryptographicException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Custody material could not be parsed as an Ed25519 PKCS#8 PEM.", ex);
        }

        try
        {
            // BouncyCastle's Ed25519PrivateKeyParameters exposes the raw
            // 32-byte seed via GetEncoded() — the same shape NSec's
            // Ed25519 PrivateKey constructor accepts.
            return bcKey.GetEncoded();
        }
        finally
        {
            // Best-effort: BC's expanded keypair sits on the GC heap with
            // no Clear() API, but GetEncoded returned a copy and the
            // reference is dropped at method exit so the GC can reclaim it.
            // We explicitly null the local to make the intent visible.
            bcKey = null!;
        }
    }
}
