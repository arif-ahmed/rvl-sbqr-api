using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.InstitutionTrust.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.InstitutionTrust.Application.Services;

/// <summary>
/// <see cref="IInstitutionTrustPublisher"/> implementation: a thin delegate
/// over <see cref="InstitutionUpsertService"/>. Keeps the trust-directory's
/// write contract, audit-row shape, and register-if-missing /
/// retire-previous-active / write-new-active semantics in exactly one
/// place (the existing upsert service).
///
/// <para>
/// Triggered by KeyCustody:
/// <list type="bullet">
///   <item><c>POST /v1/crypto-keys</c> (Generate/Adopt) →
///       <see cref="PublishActivePublicKeyAsync"/> publishes the new key
///       as ACTIVE, retiring the previous version.</item>
///   <item><c>SuspendTenantSigningKeys</c> →
///       <see cref="UpdateKeyStatusAsync"/> marks the trust row SUSPENDED.</item>
///   <item><c>ReinstateTenantSigningKeys</c> →
///       <see cref="UpdateKeyStatusAsync"/> restores the trust row to ACTIVE.</item>
/// </list>
/// </para>
///
/// The actor for all cross-module writes is
/// <see cref="IInstitutionTrustPublisher.CryptoCreateActor"/>
/// ("system:crypto-create") — same shape the trust-sync background
/// service uses — because the cross-module seam has no MediatR-captured
/// actor to attribute the side-effect to.
/// </summary>
public sealed class InstitutionTrustPublisher : IInstitutionTrustPublisher
{
    private readonly InstitutionUpsertService _upsert;

    public InstitutionTrustPublisher(InstitutionUpsertService upsert)
    {
        _upsert = upsert ?? throw new ArgumentNullException(nameof(upsert));
    }

    /// <inheritdoc/>
    public async Task<PublishedTrustKey> PublishActivePublicKeyAsync(
        string institutionCode,
        string instituteType,
        string institutionName,
        string publicKeyPem,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(instituteType);
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);

        var upserted = await _upsert
            .UpsertAsync(
                institutionCode: institutionCode,
                instituteType: instituteType,
                institutionName: institutionName,
                publicKeyPem: publicKeyPem,
                actorId: IInstitutionTrustPublisher.CryptoCreateActor,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new PublishedTrustKey(
            ActiveKeyVersion: upserted.ActiveKeyVersion,
            PublicKeySha256: upserted.PublicKeySha256);
    }

    /// <inheritdoc/>
    public Task<int> UpdateKeyStatusAsync(
        string institutionCode,
        string newStatus,
        string actorId,
        CancellationToken cancellationToken = default) =>
        _upsert.UpdateKeyStatusAsync(
            institutionCode: institutionCode,
            newStatus: newStatus,
            actorId: actorId,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public Task<string?> GetActivePublicKeySha256Async(
        string institutionCode,
        CancellationToken cancellationToken = default) =>
        _upsert.GetActivePublicKeySha256Async(institutionCode, cancellationToken);
}
