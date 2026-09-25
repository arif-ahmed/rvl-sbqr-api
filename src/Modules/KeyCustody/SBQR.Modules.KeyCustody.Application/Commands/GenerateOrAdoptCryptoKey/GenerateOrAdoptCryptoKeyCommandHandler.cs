using System.Text;
using MediatR;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Application.Contracts;
using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.Modules.KeyCustody.Domain.Interfaces;
using SBQR.Modules.Tenancy.Contracts;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Cryptography;

namespace SBQR.Modules.KeyCustody.Application.Commands.GenerateOrAdoptCryptoKey;

/// <summary>
/// Handler for <see cref="GenerateOrAdoptCryptoKeyCommand"/>. Validated input
/// is guaranteed (the <c>ValidationBehavior&lt;,&gt;</c> runs first), so this
/// method only worries about:
/// <list type="number">
///   <item>Confirming the tenant exists (via the Tenancy Contracts seam —
///         KeyCustody never touches Tenancy.Domain/Infrastructure).</item>
///   <item>Rejecting a second mint while an ACTIVE key already exists — the
///         caller must rotate via <c>PUT</c> instead.</item>
///   <item><b>Adopt-mode drift guard (two-step mandatory).</b> When
///         <c>mode = Adopt</c>, the handler derives the canonical SPKI public
///         PEM from the supplied private-key PEM, then reads
///         <c>public.institution_keys</c> for the tenant's
///         institution code. Three outcomes:
///         <list type="bullet">
///           <item><i>Trust row missing</i> — fail-closed with
///                 <c>409 InvariantViolation</c> and a
///                 <c>crypto_key.adopt.no_trust_row</c> audit row. The
///                 operator MUST pre-seed the trust row via
///                 <c>POST /v1/admin/institutions</c> before retrying Adopt.
///                 Adopt never auto-publishes a trust row on its own.</item>
///           <item><i>Trust row present but the derived pub's SHA-256 differs
///                 from the trust row's</i> — fail-closed with
///                 <c>409 InvariantViolation</c> and a
///                 <c>crypto_key.adopt.drift_rejected</c> audit row. The
///                 operator MUST either supply the matching private half or
///                 rotate the trust row via <c>POST /v1/admin/institutions</c>
///                 before retrying.</item>
///           <item><i>Trust row present and SHA-256 matches the derived
///                 pub</i> — proceed.</item>
///         </list>
///         Both failure paths run BEFORE any DB or vault side-effect.</item>
///   <item>Minting the key (Generate or Adopt), persisting it ACTIVE, and
///         then auto-publishing the public key into the InstitutionTrust
///         trust directory so <c>qr/validate</c> can resolve the tenant's
///         signature immediately. The publish is mandatory (Phase 6):
///         a failed publish returns a failure result rather than silently
///         producing a key that Verification cannot resolve. Generate and
///         Adopt both auto-publish;
///         the Adopt branch is a no-op refresh of the row already verified
///         by the drift guard.</item>
/// </list>
///
/// <para>
/// <b>Auto-publish failure contract (Phase 6 — mandatory).</b> KeyCustody and
/// InstitutionTrust own separate <c>DbContext</c>s, so there is no shared
/// transaction. The handler sequences the writes inside this method:
/// </para>
/// <list type="number">
///   <item>KeyCustody writes <c>crypto_keys</c> + vault blob and commits.</item>
///   <item>Handler calls <see cref="IInstitutionTrustPublisher.PublishActivePublicKeyAsync"/>;
///         InstitutionTrust writes <c>institution_registries</c> / <c>institution_keys</c>
///         and commits.</item>
///   <item>Audit + return.</item>
/// </list>
///
/// <para>
/// If step 2 throws (trust store unreachable, validation reject, DB outage),
/// the key row is preserved — the operator can recover with one manual
/// <c>POST /v1/admin/institutions</c> call rather than re-minting. The
/// handler emits a <c>crypto_key.trust_publish_failed</c> audit row and
/// returns a failure result; <b>no compensating delete / retire</b>. The
/// publish is mandatory: a key that exists in crypto_keys but not in the
/// trust directory cannot be verified by <c>qr/validate</c> (which reads
/// only InstitutionTrust) and would produce spurious KEY_NOT_FOUND
/// verdicts — so a failed publish is a hard failure of the mint operation.
/// </para>
///
/// <para>
/// <b>Audit shape.</b>
/// <list type="bullet">
///   <item><c>crypto_key.minted</c> on a successful Generate,</item>
///   <item><c>crypto_key.adopted</c> on a successful Adopt (drift-check passed),</item>
///   <item><c>crypto_key.adopt.no_trust_row</c> on an Adopt whose
///         institution has no ACTIVE trust row in
///         <c>public.institution_keys</c> (no DB or vault
///         side-effect),</item>
///   <item><c>crypto_key.adopt.drift_rejected</c> on an Adopt whose
///         derived-from-priv public SHA-256 does not match the active trust
///         row's SHA-256 (no DB or vault side-effect),</item>
///   <item><c>crypto_key.trust_publish_failed</c> when the DB commit succeeds
///         but the auto-publish to <c>institution_keys</c> throws.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>2026-09-10 Adopt-mode refactor.</b> The previous Adopt contract
/// required the operator to paste both PEM halves in the request body and
/// cross-checked the supplied public against the supplied private. The
/// refactor drops <c>publicKeyPem</c> from the request body entirely: only
/// the private-key PEM is supplied, the public half is derived, and the
/// drift guard compares the derived pub against the trust row. This makes
/// the half-pair mismatch path (previously <c>EC-Adopt-2</c>) impossible
/// by construction — there is no longer a second half to mismatch.
/// </para>
/// </summary>
public sealed class GenerateOrAdoptCryptoKeyCommandHandler
    : IRequestHandler<GenerateOrAdoptCryptoKeyCommand, Result<CryptoKeySummary>>
{
    private readonly ITenantDirectory _tenants;
    private readonly ICryptoKeyRepository _keys;
    private readonly IKeyPairGenerator _generator;
    private readonly IKeyPairValidator _validator;
    private readonly ISigningKeyStore _vault;
    private readonly IKeyCustodyUnitOfWork _uow;
    private readonly IInstitutionTrustPublisher _trust;
    private readonly IActorProvider _actor;
    private readonly IAuditLogger _audit;

    public GenerateOrAdoptCryptoKeyCommandHandler(
        ITenantDirectory tenants,
        ICryptoKeyRepository keys,
        IKeyPairGenerator generator,
        IKeyPairValidator validator,
        ISigningKeyStore vault,
        IKeyCustodyUnitOfWork uow,
        IInstitutionTrustPublisher trust,
        IActorProvider actor,
        IAuditLogger audit)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<Result<CryptoKeySummary>> Handle(
        GenerateOrAdoptCryptoKeyCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = await _tenants
            .LookupAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<CryptoKeySummary>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId:D} does not exist.");
        }

        var existingActive = await _keys
            .GetActiveByTenantAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (existingActive is not null)
        {
            return Result<CryptoKeySummary>.Failure(
                ErrorCode.InvariantViolation,
                $"tenant {request.TenantId:D} already has an ACTIVE signing key " +
                $"(key {existingActive.KeyId} v{existingActive.KeyVersion}); use PUT to rotate instead.");
        }

        CryptoKey key;
        try
        {
            key = request.Mode == CryptoKeyMode.Adopt
                ? BuildAdopted(request, tenant.InstitutionCode)
                : BuildGenerated(request.TenantId, tenant.InstitutionCode);
        }
        catch (ArgumentException ex)
        {
            return Result<CryptoKeySummary>.Failure(ErrorCode.InvariantViolation, ex.Message);
        }

        // Adopt-mode drift guard (two-step mandatory): read the active trust
        // row for this institution code and compare its public_key_sha256
        // against the SHA-256 of the public half the validator derived from
        // the supplied private. Three outcomes — see the class XML doc.
        if (request.Mode == CryptoKeyMode.Adopt)
        {
            var existingTrustHash = await _trust
                .GetActivePublicKeySha256Async(tenant.InstitutionCode, cancellationToken)
                .ConfigureAwait(false);

            if (existingTrustHash is null)
            {
                var derivedShort = ShortHash(key.PublicKeySha256);

                await _audit.LogAsync(new AuditEntry(
                    Action: "crypto_key.adopt.no_trust_row",
                    ActorId: _actor.CurrentActor(),
                    ResourceType: "Tenant",
                    ResourceId: request.TenantId.ToString(),
                    Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"institution_code\":\"{tenant.InstitutionCode}\",\"derived_pk_sha256\":\"{key.PublicKeySha256}\"}}",
                    TenantId: request.TenantId),
                    cancellationToken).ConfigureAwait(false);

                return Result<CryptoKeySummary>.Failure(
                    ErrorCode.InvariantViolation,
                    $"no trust row for institute {tenant.InstitutionCode}: public.institution_keys " +
                    $"has no ACTIVE row for this institution. Pre-seed the trust row via " +
                    $"POST /v1/admin/institutions (with the public half matching the private key you just " +
                    $"supplied — derived SHA-256={derivedShort}…) and retry Adopt. Adopt never auto-publishes " +
                    $"a trust row on its own: the public-key half must already be in the trust directory.");
            }

            if (!string.Equals(existingTrustHash, key.PublicKeySha256, StringComparison.Ordinal))
            {
                var existingShort = ShortHash(existingTrustHash);
                var derivedShort = ShortHash(key.PublicKeySha256);

                await _audit.LogAsync(new AuditEntry(
                    Action: "crypto_key.adopt.drift_rejected",
                    ActorId: _actor.CurrentActor(),
                    ResourceType: "Tenant",
                    ResourceId: request.TenantId.ToString(),
                    Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"institution_code\":\"{tenant.InstitutionCode}\",\"existing_pk_sha256\":\"{existingTrustHash}\",\"derived_pk_sha256\":\"{key.PublicKeySha256}\"}}",
                    TenantId: request.TenantId),
                    cancellationToken).ConfigureAwait(false);

                return Result<CryptoKeySummary>.Failure(
                    ErrorCode.InvariantViolation,
                    $"trust_store public-key drift: institute {tenant.InstitutionCode} already has a trusted " +
                    $"public key (sha256={existingShort}…) which does NOT match the public key DERIVED from " +
                    $"the supplied private half (sha256={derivedShort}…). Either supply the matching private " +
                    $"half or rotate the trust row explicitly via POST /v1/admin/institutions before retrying.");
            }
        }

        await _keys.AddAsync(key, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Emit the success-path audit row BEFORE the auto-publish so a
        // trust-publish failure can still distinguish "minted but not
        // published" (crypto_key.trust_publish_failed) from "minted and
        // published" (crypto_key.minted / crypto_key.adopted, which is the
        // current row). Generate and Adopt get distinct action labels so
        // operators can tell which mint path produced the row.
        await _audit.LogAsync(new AuditEntry(
            Action: request.Mode == CryptoKeyMode.Adopt ? "crypto_key.adopted" : "crypto_key.minted",
            ActorId: _actor.CurrentActor(),
            ResourceType: "CryptoKey",
            ResourceId: key.Id.Value.ToString(),
            Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"institution_code\":\"{tenant.InstitutionCode}\",\"public_key_sha256\":\"{key.PublicKeySha256}\",\"key_version\":{key.KeyVersion}}}",
            TenantId: request.TenantId),
            cancellationToken).ConfigureAwait(false);

        // Step 2: auto-publish the freshly minted public key into the
        // InstitutionTrust trust directory so qr/validate can resolve this
        // tenant's signature immediately. This is a MANDATORY step (Phase 6):
        // if the publish fails, the key cannot be verified by Verification
        // (which reads only InstitutionTrust) and the mint returns a failure.
        // The crypto_keys row is preserved — the operator can recover with
        // one manual POST /v1/admin/institutions call rather than re-minting.
        //
        // The publisher seam takes the trust-directory wire data
        // (institutionCode + instituteType + institutionName + publicKeyPem).
        // The institution name is already on TenantPublicInfo (KeyCustody
        // resolved the tenant at the top of this method) and the Annex A
        // Institution Type is derived from institution_code[..2] — so we
        // carry both across the seam rather than re-reading from tenancy.
        // The auto-publish will fail at the upsert's PEM-shape guard if
        // the mint produced a non-PEM key, which surfaces as the
        // trust_publish_failed audit row below.
        try
        {
            // InstitutionType is the BB InstitutionId prefix per
            // docs/design/qr-annex-a.md (Tag 26 sub 01): the first two
            // digits of the six-digit institution code. Derived via the
            // InstitutionId value object so the spec's
            // Institution_ID formula lives in one place.
            var institutionId = SharedKernel.QrCodec.InstitutionId.FromCode(tenant.InstitutionCode);

            await _trust
                .PublishActivePublicKeyAsync(
                    institutionCode: institutionId.Value,
                    instituteType: institutionId.InstitutionType,
                    institutionName: tenant.InstitutionName,
                    publicKeyPem: key.PublicKey,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _audit.LogAsync(new AuditEntry(
                Action: "crypto_key.trust_publish_failed",
                ActorId: _actor.CurrentActor(),
                ResourceType: "CryptoKey",
                ResourceId: key.Id.Value.ToString(),
                Metadata: $"{{\"tenant_id\":\"{request.TenantId:D}\",\"public_key_sha256\":\"{key.PublicKeySha256}\",\"reason\":\"{ex.GetType().Name}: {ex.Message}\"}}",
                TenantId: request.TenantId),
                cancellationToken).ConfigureAwait(false);

            return Result<CryptoKeySummary>.Failure(
                ErrorCode.InvariantViolation,
                $"key created but trust-store publish failed ({ex.GetType().Name}: {ex.Message}). " +
                $"Use POST /v1/admin/institutions to register the public key manually.");
        }

        return Result<CryptoKeySummary>.Ok(CryptoKeySummaryBuilder.Build(key));
    }

    private static string ShortHash(string fullHexSha256) =>
        fullHexSha256.Length >= 12 ? fullHexSha256[..12] : fullHexSha256;

    private CryptoKey BuildGenerated(Guid tenantId, string institutionCode)
    {
        var (publicKeyPem, privateKeyBytes) = _generator.GenerateEd25519();
        return CryptoKey.Generate(
            tenantId: tenantId,
            keyId: CryptoKey.DefaultKeyId,
            institutionCode: institutionCode,
            publicKeyPem: publicKeyPem,
            privateKeyBytes: privateKeyBytes,
            vault: _vault);
    }

    private CryptoKey BuildAdopted(GenerateOrAdoptCryptoKeyCommand request, string institutionCode)
    {
        if (string.IsNullOrWhiteSpace(request.PrivateKeyPem))
        {
            throw new ArgumentException(
                "'privateKeyPem' is required when mode is 'Adopt'.");
        }

        // Derive the canonical SPKI public-key PEM from the supplied
        // private-key PEM (RFC 8032 scalar-base-mult of the seed). The
        // drift guard in Handle() will then compare its SHA-256 against the
        // trust row's public_key_sha256 before any DB or vault side-effect.
        var canonicalPublicPem = _validator.DerivePublicKeyPem(request.PrivateKeyPem);
        var privateKeyPemBytes = Encoding.ASCII.GetBytes(request.PrivateKeyPem);

        return CryptoKey.Adopt(
            tenantId: request.TenantId,
            keyId: CryptoKey.DefaultKeyId,
            institutionCode: institutionCode,
            publicKeyPem: canonicalPublicPem,
            privateKeyPemBytes: privateKeyPemBytes,
            vault: _vault);
    }
}
