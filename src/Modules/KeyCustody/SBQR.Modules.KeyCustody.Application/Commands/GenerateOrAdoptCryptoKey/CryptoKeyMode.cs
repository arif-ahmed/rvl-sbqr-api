namespace SBQR.Modules.KeyCustody.Application.Commands.GenerateOrAdoptCryptoKey;

/// <summary>
/// Which minting scenario <see cref="GenerateOrAdoptCryptoKeyCommand"/> should
/// run: server-generated (<see cref="Generate"/>) or caller-supplied PEM pair
/// (<see cref="Adopt"/>). Mirrors <c>CryptoKey.Generate</c> / <c>CryptoKey.Adopt</c>.
/// </summary>
public enum CryptoKeyMode
{
    /// <summary>Server generates a fresh Ed25519 keypair.</summary>
    Generate = 0,

    /// <summary>Caller supplies both halves of an Ed25519 keypair as PEM.</summary>
    Adopt = 1,
}
