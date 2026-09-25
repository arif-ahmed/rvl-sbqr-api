// ISigningProvider is the abstraction. The implementation lives here, inside
// the cryptographic boundary. The shared-kernel interface is re-exported as
// a using so callers don't have to add a second dependency.

namespace SBQR.Modules.KeyCustody.Infrastructure.Custody;

using SBQR.SharedKernel.Cryptography;

// Concrete implementations of ISigningProvider live here.
// PlainFileSigningProvider is dev-only and must be hard-blocked in Production
// by the host. The real Phase-1 provider is PemVaultSigningProvider (this
// folder); the raw-sign unit tests in SBQR.Modules.KeyCustody.Tests cover
// the Ed25519 round-trip against BouncyCastle's reference implementation.

/// <summary>
/// Plaintext file-based signing provider. DEV-ONLY. Must never be registered
/// in Production. The host enforces this by checking
/// <see cref="IHostEnvironment.IsProduction"/> at startup and throwing if
/// this provider is the resolved <see cref="ISigningProvider"/>.
/// </summary>
public sealed class PlainFileSigningProvider : ISigningProvider
{
    public string ProviderId => "plain-file-dev";

    public Task<byte[]> SignAsync(byte[] payload, CancellationToken cancellationToken = default)
        =>  throw new NotImplementedException(
            "PlainFileSigningProvider is a dev-only stub. Select KeyCustody:ActiveProvider=PemVault for real signing.");
}

/// <summary>
/// Phase-2 target. PKCS#11 against a network-attached HSM (Thales / Utimaco
/// / AWS CloudHSM). Drops in behind the same ISigningProvider port.
/// </summary>
public sealed class HsmSigningProvider : ISigningProvider
{
    public string ProviderId => "hsm";

    public Task<byte[]> SignAsync(byte[] payload, CancellationToken cancellationToken = default)
        => throw new NotImplementedException(
            "HsmSigningProvider is a Phase-2 target.");
}
