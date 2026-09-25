using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SBQR.Modules.KeyCustody.Domain.Events;
using SBQR.SharedKernel.Cryptography;
using SBQR.SharedKernel.Domain;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.KeyCustody.Domain.Aggregates;

/// <summary>
/// Aggregate root for a tenant's signing key. KeyCustody owns the full
/// lifecycle: minting (Generate/Adopt), cascade suspend/reinstate, and
/// rotation (Retire the old version, mint a new one).
///
/// Lifecycle:
/// <list type="bullet">
///   <item><see cref="Generate"/> — Scenario A: no caller-supplied keys. The
///         factory generates an Ed25519 keypair, stashes the private bytes in
///         the vault via the supplied <see cref="ISigningKeyStore"/>, mints an
///         opaque custody handle, and returns a <see cref="CryptoKey"/> with
///         <see cref="Status"/> = <see cref="CryptoKeyStatus.Active"/>.</item>
///   <item><see cref="Adopt"/> — Scenario B: caller supplies PEMs. The factory
///         stashes the private PEM bytes in the vault behind a custody handle
///         and records the public-key PEM. The handler (in
///         <c>Application</c>) is responsible for parsing both PEMs and
///         confirming the supplied public matches the public derived from the
///         private before calling this factory; the Domain only stores the
///         opaque private-bytes blob.</item>
/// </list>
///
/// Invariants:
/// <list type="number">
///   <item><c>key_version</c> starts at <c>1</c>; rotation
///         (<see cref="Retire"/> the old row + <see cref="Generate"/>/<see cref="Adopt"/>
///         a new one at <c>oldVersion + 1</c>) is orchestrated by the
///         Application layer, not by this aggregate.</item>
///   <item><c>public_key</c> is a PEM-encoded Ed25519 SPKI blob.</item>
///   <item><c>tenant_id</c> is set at issuance and immutable.</item>
///   <item>The private key bytes never escape this aggregate — only the
///         <c>custody_key_reference</c> (handle) is persisted.</item>
/// </list>
/// </summary>
public sealed class CryptoKey : AggregateRoot<CryptoKeyId>, IAuditableEntity
{
    /// <summary>
    /// Logical signing-key name; the database-design.md §1.4 sample uses
    /// <c>sbqr-signing</c>. Callers (tests included) refer to the same identifier.
    /// </summary>
    public const string DefaultKeyId = "sbqr-signing";

    private static readonly Regex PemEndsPattern = new(
        pattern: @"-----BEGIN [A-Z ]+-----[\s\S]+-----END [A-Z ]+-----",
        options: RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Constructor is private: only the static factories can build a CryptoKey.
    // EF Core binds constructor parameters to properties by name; the publicKey
    // parameter matches the PublicKey property so the EF Core model hydration
    // can reconstruct the aggregate from a row.
    private CryptoKey(
        CryptoKeyId id,
        Guid tenantId,
        string keyId,
        int keyVersion,
        string publicKey,
        string custodyKeyReference,
        CryptoKeyStatus status)
        : base(id)
    {
        TenantId = tenantId;
        KeyId = keyId;
        KeyVersion = keyVersion;
        PublicKey = publicKey;
        CustodyKeyReference = custodyKeyReference;
        Status = status;
        IsActive = true;
    }

    /// <summary>The owning tenant; immutable for the aggregate's lifetime.</summary>
    public Guid TenantId { get; }

    /// <summary>
    /// Logical signing-key name. <see cref="DefaultKeyId"/> for the register
    /// flow's first key.
    /// </summary>
    public string KeyId { get; }

    /// <summary>Monotonic version, starts at 1.</summary>
    public int KeyVersion { get; }

    /// <summary>PEM-encoded Ed25519 SPKI public key.</summary>
    public string PublicKey { get; }

    /// <summary>
    /// Opaque handle resolved through <see cref="ISigningKeyStore"/>. The DB
    /// stores this column; the wrapped private bytes never appear in any row.
    /// </summary>
    public string CustodyKeyReference { get; }

    /// <summary>Lifecycle state — see <see cref="CryptoKeyStatus"/>.</summary>
    public CryptoKeyStatus Status { get; private set; }

    /// <summary>
    /// Soft-delete flag. <c>false</c> means the row is preserved for history
    /// but excluded from active queries (partial index <c>ix_*_active</c>).
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// SHA-256 fingerprint of the SPKI public-key bytes — same shape as
    /// <c>institution_keys.public_key_sha256</c> in
    /// <c>docs/design/database-design.md</c> §1.3.
    /// </summary>
    public string PublicKeySha256 { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public string? CreatedBy { get; set; }

    /// <inheritdoc/>
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc/>
    public string? ModifiedBy { get; set; }

    /// <inheritdoc/>
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>
    /// Scenario A: store the supplied (already-generated) Ed25519 public PEM
    /// and wrap the private bytes through the vault, returning a fully-formed
    /// <see cref="CryptoKey"/> with <see cref="Status"/> =
    /// <see cref="CryptoKeyStatus.Active"/>. The factory never sees the
    /// algorithm choices — those are the caller's job — it only persists.
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="keyId">Logical signing-key id (use <see cref="DefaultKeyId"/>).</param>
    /// <param name="institutionCode">Six-digit BB-assigned institution code
    /// (<c>tenants.institution_code</c>). Encoded into the custody handle so
    /// the S3 vault can produce operator-readable object keys
    /// (<c>keys/&lt;code&gt;_v&lt;n&gt;_private.pem</c>) without an extra
    /// lookup. Ignored by the Local file vault, which hashes the whole handle.</param>
    /// <param name="publicKeyPem">SPKI public-key PEM (Ed25519).</param>
    /// <param name="privateKeyBytes">Raw private-key bytes that the vault will wrap and store.</param>
    /// <param name="vault">Signing-key store that wraps and persists the private bytes.</param>
    /// <param name="keyVersion">Monotonic version for this key row. Defaults to <c>1</c>
    /// (first key); rotation callers pass <c>oldVersion + 1</c>.</param>
    public static CryptoKey Generate(
        Guid tenantId,
        string keyId,
        string institutionCode,
        string publicKeyPem,
        ReadOnlySpan<byte> privateKeyBytes,
        ISigningKeyStore vault,
        int keyVersion = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ValidateInstitutionCode(institutionCode);
        ArgumentNullException.ThrowIfNull(vault);
        ValidatePem(publicKeyPem, "public_key_pem");

        var provisionalHandle = ComposeCustodyHandle(tenantId, institutionCode, keyId, keyVersion);
        var handle = vault.Store(tenantId, privateKeyBytes, provisionalHandle);

        var key = new CryptoKey(
            id: new CryptoKeyId(Guid.NewGuid()),
            tenantId: tenantId,
            keyId: keyId,
            keyVersion: keyVersion,
            publicKey: publicKeyPem,
            custodyKeyReference: handle,
            status: CryptoKeyStatus.Active);

        key.PublicKeySha256 = ComputeSha256Hex(publicKeyPem);

        key.RaiseDomainEvent(new CertificateGenerated(
            TenantId: tenantId,
            CryptoKeyId: key.Id,
            KeyId: key.KeyId,
            KeyVersion: key.KeyVersion,
            PublicKeySha256: key.PublicKeySha256,
            OccurredAt: DateTimeOffset.UtcNow));

        return key;
    }

    /// <summary>
    /// Scenario B: adopt a tenant-supplied keypair. The handler has already
    /// validated PEM consistency; the factory stores the PEM bytes verbatim
    /// through the vault and records the public PEM. The custody handle
    /// shape is identical to a Generate-mode handle — Adopt overwrites
    /// Generate in place at the same (institution, version).
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="keyId">Logical signing-key id.</param>
    /// <param name="institutionCode">Six-digit BB-assigned institution code
    /// (<c>tenants.institution_code</c>). Encoded into the custody handle so
    /// the S3 vault can produce operator-readable object keys.</param>
    /// <param name="publicKeyPem">SPKI public-key PEM (Ed25519).</param>
    /// <param name="privateKeyPemBytes">Raw private-key PEM bytes (the whole
    ///     PEM block including BEGIN/END markers) — the vault stores them
    ///     as-is.</param>
    /// <param name="vault">Signing-key store that wraps and persists the private bytes.</param>
    /// <param name="keyVersion">Monotonic version for this key row. Defaults to <c>1</c>.</param>
    public static CryptoKey Adopt(
        Guid tenantId,
        string keyId,
        string institutionCode,
        string publicKeyPem,
        byte[] privateKeyPemBytes,
        ISigningKeyStore vault,
        int keyVersion = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ValidateInstitutionCode(institutionCode);
        ArgumentNullException.ThrowIfNull(privateKeyPemBytes);
        ArgumentNullException.ThrowIfNull(vault);
        ValidatePem(publicKeyPem, "public_key_pem");

        var provisionalHandle = ComposeCustodyHandle(tenantId, institutionCode, keyId, keyVersion);
        var handle = vault.Store(tenantId, privateKeyPemBytes, provisionalHandle);

        var key = new CryptoKey(
            id: new CryptoKeyId(Guid.NewGuid()),
            tenantId: tenantId,
            keyId: keyId,
            keyVersion: keyVersion,
            publicKey: publicKeyPem,
            custodyKeyReference: handle,
            status: CryptoKeyStatus.Active);

        key.PublicKeySha256 = ComputeSha256Hex(publicKeyPem);

        key.RaiseDomainEvent(new CertificateAdopted(
            TenantId: tenantId,
            CryptoKeyId: key.Id,
            KeyId: key.KeyId,
            KeyVersion: key.KeyVersion,
            PublicKeySha256: key.PublicKeySha256,
            OccurredAt: DateTimeOffset.UtcNow));

        return key;
    }

    /// <summary>
    /// Cascade-suspend the key. Invoked when the owning tenant is suspended
    /// or terminated. Throws on self-transition; the DB CHECK constraint also
    /// rejects any other state.
    /// </summary>
    public void Suspend()
    {
        if (Status == CryptoKeyStatus.Suspended)
        {
            throw new InvalidOperationException(
                $"CryptoKey {Id} is already Suspended; Suspend is a no-op and must not raise a duplicate event.");
        }

        if (Status != CryptoKeyStatus.Active)
        {
            throw new InvalidOperationException(
                $"CryptoKey {Id} cannot be Suspended from '{Status}'; only ACTIVE keys can be suspended.");
        }

        Status = CryptoKeyStatus.Suspended;

        RaiseDomainEvent(new KeySuspended(
            TenantId: TenantId,
            CryptoKeyId: Id,
            OccurredAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Cascade-reinstate the key. Invoked when the owning tenant is
    /// reactivated. Only <see cref="CryptoKeyStatus.Suspended"/> keys can be
    /// reinstated; any other state (e.g. <see cref="CryptoKeyStatus.Retired"/>)
    /// is a coding error, not a recoverable transition.
    /// </summary>
    public void Reinstate()
    {
        if (Status == CryptoKeyStatus.Active)
        {
            throw new InvalidOperationException(
                $"CryptoKey {Id} is already Active; Reinstate is a no-op and must not raise a duplicate event.");
        }

        if (Status != CryptoKeyStatus.Suspended)
        {
            throw new InvalidOperationException(
                $"CryptoKey {Id} cannot be Reinstated from '{Status}'; only SUSPENDED keys can be reinstated.");
        }

        Status = CryptoKeyStatus.Active;

        RaiseDomainEvent(new KeyReinstated(
            TenantId: TenantId,
            CryptoKeyId: Id,
            OccurredAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Take the key out of service — the rotation counterpart to
    /// <see cref="Generate"/>/<see cref="Adopt"/> minting a replacement.
    /// Retired keys still verify historical QRs (Verification treats RETIRED
    /// as a passing status); they simply stop being eligible for new signing.
    /// Only <see cref="CryptoKeyStatus.Active"/> or
    /// <see cref="CryptoKeyStatus.Suspended"/> keys can be retired.
    /// </summary>
    public void Retire()
    {
        if (Status == CryptoKeyStatus.Retired)
        {
            throw new InvalidOperationException(
                $"CryptoKey {Id} is already Retired; Retire is a no-op and must not raise a duplicate event.");
        }

        if (Status != CryptoKeyStatus.Active && Status != CryptoKeyStatus.Suspended)
        {
            throw new InvalidOperationException(
                $"CryptoKey {Id} cannot be Retired from '{Status}'; only ACTIVE or SUSPENDED keys can be retired.");
        }

        Status = CryptoKeyStatus.Retired;

        RaiseDomainEvent(new KeyRetired(
            TenantId: TenantId,
            CryptoKeyId: Id,
            OccurredAt: DateTimeOffset.UtcNow));
    }

    private static void ValidatePem(string pem, string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pem);
        if (!PemEndsPattern.IsMatch(pem))
        {
            throw new ArgumentException(
                $"'{fieldName}' must look like a PEM block (-----BEGIN …----- … -----END …-----).",
                fieldName);
        }
    }

    /// <summary>
    /// Validates a six-digit Bangladesh Bank institution code
    /// (<c>tenants.institution_code CHAR(6) CHECK (institution_code ~ '^[0-9]{6}$')</c>).
    /// Same regex the DB CHECK constraint enforces; the in-process check lets the
    /// Domain surface a clean <see cref="ArgumentException"/> instead of a
    /// raw PG exception when the upstream value is bad.
    /// </summary>
    private static void ValidateInstitutionCode(string institutionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionCode);
        if (institutionCode.Length != 6 || !System.Text.RegularExpressions.Regex.IsMatch(
                institutionCode, "^[0-9]{6}$"))
        {
            throw new ArgumentException(
                $"institutionCode must be exactly six digits (BB-assigned code); got '{institutionCode}'.",
                nameof(institutionCode));
        }
    }

    /// <summary>
    /// Composes the opaque custody handle stored in
    /// <c>crypto_keys.custody_key_reference</c>. Shape:
    /// <c>tenant:&lt;guid&gt;:institution:&lt;6digit&gt;:&lt;keyId&gt;:v&lt;n&gt;</c>.
    /// The same shape is used by Generate and Adopt — an Adopt overwrites
    /// any prior Generate blob at the same (institution, version). The
    /// institution code is embedded so the S3 vault can produce
    /// operator-readable object keys
    /// (<c>keys/&lt;code&gt;_v&lt;n&gt;_private.pem</c>) without a runtime
    /// cross-module lookup.
    /// </summary>
    private static string ComposeCustodyHandle(
        Guid tenantId,
        string institutionCode,
        string keyId,
        int keyVersion)
    {
        ValidateInstitutionCode(institutionCode);

        return $"tenant:{tenantId:D}:institution:{institutionCode}:{keyId}:v{keyVersion}";
    }

    private static string ComputeSha256Hex(string pem)
    {
        var bytes = Encoding.ASCII.GetBytes(pem);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
