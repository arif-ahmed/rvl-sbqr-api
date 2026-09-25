using MediatR;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.KeyCustody.Application.Commands.GenerateOrAdoptCryptoKey;

/// <summary>
/// Mint the tenant's first signing key. <c>POST /v1/crypto-keys</c>.
/// Fails with <see cref="ErrorCode.NotFound"/> if the tenant does not exist,
/// and <see cref="ErrorCode.InvariantViolation"/> if the tenant already has
/// an ACTIVE key — callers rotate via <c>PUT /v1/crypto-keys/{tenantId}</c>
/// instead of re-POSTing.
/// </summary>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="Mode"><see cref="CryptoKeyMode.Generate"/> (server-minted) or
/// <see cref="CryptoKeyMode.Adopt"/> (caller-supplied private-key PEM only;
/// the public half is read from <c>public.institution_keys</c> by
/// the drift guard).</param>
/// <param name="PrivateKeyPem">Required when <paramref name="Mode"/> is
/// <see cref="CryptoKeyMode.Adopt"/>; unused for <see cref="CryptoKeyMode.Generate"/>.</param>
public sealed record GenerateOrAdoptCryptoKeyCommand(
    Guid TenantId,
    CryptoKeyMode Mode,
    string? PrivateKeyPem = null) : IRequest<Result<CryptoKeySummary>>;
