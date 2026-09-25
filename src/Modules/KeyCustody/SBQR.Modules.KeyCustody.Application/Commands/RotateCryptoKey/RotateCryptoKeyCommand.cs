using MediatR;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.KeyCustody.Application.Commands.RotateCryptoKey;

/// <summary>
/// Rotate the tenant's signing key: retire the current ACTIVE version and
/// mint a replacement at <c>oldVersion + 1</c>.
/// <c>PUT /v1/crypto-keys/{tenantId}</c>. Server-generates only (no Adopt-on-rotate).
/// Skeleton: handler returns <see cref="ErrorCode.InvariantViolation"/> stub failure.
/// </summary>
/// <param name="TenantId">Owning tenant.</param>
public sealed record RotateCryptoKeyCommand(
    Guid TenantId) : IRequest<Result<CryptoKeySummary>>;
