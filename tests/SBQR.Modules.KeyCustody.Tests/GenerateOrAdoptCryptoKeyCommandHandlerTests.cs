using System.Collections.Concurrent;
using FluentAssertions;
using NSubstitute;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Application.Commands.GenerateOrAdoptCryptoKey;
using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.Modules.KeyCustody.Domain.Interfaces;
using SBQR.Modules.KeyCustody.Infrastructure.Cryptography;
using SBQR.Modules.Tenancy.Contracts;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Cryptography;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// Tests for the auto-publish side-effect on <see cref="GenerateOrAdoptCryptoKeyCommandHandler"/>:
/// a successful mint must publish the public key into the trust directory
/// (delegated through <see cref="IInstitutionTrustPublisher"/>), and a
/// trust-publish failure must audit a <c>crypto_key.trust_publish_failed</c>
/// row rather than swallow or silently roll back.
///
/// <para>
/// 2026-09-10 Adopt-mode refactor: <c>Adopt</c> now requires only the
/// private-key PEM in the request body. The handler derives the canonical
/// public-key PEM via <see cref="IKeyPairValidator.DerivePublicKeyPem"/>
/// and runs a drift guard against the trust row in
/// <c>public.institution_keys</c>. Two failure modes:
/// </para>
/// <list type="bullet">
///   <item>missing trust row → <c>crypto_key.adopt.no_trust_row</c></item>
///   <item>trust row present but its pub sha256 differs → <c>crypto_key.adopt.drift_rejected</c></item>
/// </list>
/// </summary>
public sealed class GenerateOrAdoptCryptoKeyCommandHandlerTests
{
    private const string TenantInstitutionCode = "010101";
    private const string TenantInstitutionName = "Example Bank";

    private readonly ITenantDirectory _tenants = Substitute.For<ITenantDirectory>();
    private readonly ICryptoKeyRepository _keys = Substitute.For<ICryptoKeyRepository>();
    private readonly IKeyPairGenerator _generator = new Ed25519KeyPairGenerator();
    private readonly IKeyPairValidator _validator = new Ed25519PEMValidator();
    private readonly InMemoryVault _vault = new();
    private readonly IKeyCustodyUnitOfWork _uow = Substitute.For<IKeyCustodyUnitOfWork>();
    private readonly IInstitutionTrustPublisher _trust = Substitute.For<IInstitutionTrustPublisher>();
    private readonly IActorProvider _actor = Substitute.For<IActorProvider>();
    private readonly CapturingAudit _audit = new();

    private GenerateOrAdoptCryptoKeyCommandHandler CreateSut() => new(
        _tenants, _keys, _generator, _validator, _vault, _uow, _trust, _actor, _audit);

    private void StubTenant(Guid tenantId) =>
        _tenants.LookupAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantPublicInfo(tenantId, TenantInstitutionCode, TenantInstitutionName, TenantAdmissionState.Pending));

    [Fact]
    public async Task Successful_mint_publishes_public_key_to_trust_store()
    {
        var tenantId = Guid.NewGuid();
        StubTenant(tenantId);
        _keys.GetActiveByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns((CryptoKey?)null);
        _trust.PublishActivePublicKeyAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PublishedTrustKey(ActiveKeyVersion: 1, PublicKeySha256: "abc123")));

        var sut = CreateSut();
        var result = await sut.Handle(
            new GenerateOrAdoptCryptoKeyCommand(tenantId, CryptoKeyMode.Generate),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.PublicKeyPem.Should().NotBeNullOrWhiteSpace();
        result.Value.CryptoKeyId.Should().NotBe(Guid.Empty);

        await _trust.Received(1).PublishActivePublicKeyAsync(
            TenantInstitutionCode,
            "01", // tenant institution_code prefix 010101 -> "01"
            TenantInstitutionName,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Trust_publish_failure_emits_audit_row_and_returns_failure_without_rolling_back()
    {
        var tenantId = Guid.NewGuid();
        StubTenant(tenantId);
        _keys.GetActiveByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns((CryptoKey?)null);
        _trust.PublishActivePublicKeyAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PublishedTrustKey>(new InvalidOperationException("trust store unavailable")));
        _actor.CurrentActor().Returns("ops-bob");

        var sut = CreateSut();
        var result = await sut.Handle(
            new GenerateOrAdoptCryptoKeyCommand(tenantId, CryptoKeyMode.Generate),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("trust-store publish failed");
        result.ErrorMessage.Should().Contain("Use POST /v1/admin/institutions");

        // The audit row MUST be present so operators see the partial state.
        // The crypto_keys row was already committed (we cannot unsave it
        // here without a real DbContext) — the unit-of-work SaveChangesAsync
        // is the seam that proves step 1 happened before step 2 threw.
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var entries = _audit.Entries;
        var dump = string.Join("\n", entries.Select(e => $"Action={e.Action} Resource={e.ResourceType} Tenant={e.TenantId} Meta={e.Metadata}"));
        var failedEntries = entries.Where(e => e.Action == "crypto_key.trust_publish_failed").ToList();
        failedEntries.Should().ContainSingle("exactly one crypto_key.trust_publish_failed row. Entries:\n" + dump);
        var failed = failedEntries[0];
        failed.ResourceType.Should().Be("CryptoKey");
        failed.TenantId.Should().Be(tenantId);
        failed.Metadata.Should().NotBeNull();
        failed.Metadata!.Should().Contain("public_key_sha256");
        failed.Metadata!.Should().Contain("trust store unavailable");
    }

    [Fact]
    public async Task Tenant_not_found_returns_404_and_does_not_call_publisher()
    {
        var tenantId = Guid.NewGuid();
        _tenants.LookupAsync(tenantId, Arg.Any<CancellationToken>()).Returns((TenantPublicInfo?)null);

        var sut = CreateSut();
        var result = await sut.Handle(
            new GenerateOrAdoptCryptoKeyCommand(tenantId, CryptoKeyMode.Generate),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);

        await _trust.DidNotReceiveWithAnyArgs().PublishActivePublicKeyAsync(default!, default!, default!, default!, default);
        _audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_active_key_returns_409_and_does_not_call_publisher()
    {
        var tenantId = Guid.NewGuid();
        StubTenant(tenantId);
        var existingKeyId = Guid.NewGuid();
        _keys.GetActiveByTenantAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(CryptoKey.Generate(
                tenantId: tenantId,
                keyId: CryptoKey.DefaultKeyId,
                institutionCode: TenantInstitutionCode,
                publicKeyPem: "-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----",
                privateKeyBytes: new byte[32],
                vault: _vault));

        var sut = CreateSut();
        var result = await sut.Handle(
            new GenerateOrAdoptCryptoKeyCommand(tenantId, CryptoKeyMode.Generate),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("already has an ACTIVE signing key");

        await _trust.DidNotReceiveWithAnyArgs().PublishActivePublicKeyAsync(default!, default!, default!, default!, default);
    }

    /// <summary>Test double for <see cref="ISigningKeyStore"/>.</summary>
    private sealed class InMemoryVault : ISigningKeyStore
    {
        public string Store(Guid tenantId, ReadOnlySpan<byte> privateKeyBytes, string suggestedHandle) => suggestedHandle;
        public byte[]? TryLoad(string custodyHandle) => null;
    }

    /// <summary>Test double for <see cref="IAuditLogger"/>.</summary>
    private sealed class CapturingAudit : IAuditLogger
    {
        private readonly ConcurrentQueue<AuditEntry> _entries = new();
        public IReadOnlyList<AuditEntry> Entries => _entries.ToArray();

        public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            _entries.Enqueue(entry);
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // FR-TENANT-001 §1.4 — Adopt-mode tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Helper: mint a fresh Ed25519 PEM pair locally for Adopt payloads
    /// (we never call the server-side generator in Adopt mode). After the
    /// 2026-09-10 refactor the request body only carries the private-key
    /// PEM, but the helper still returns both so tests can compute the
    /// derived-from-priv sha256 when they need to stub the trust row.
    /// </summary>
    private static (string PrivatePem, string PublicPem) NewAdoptPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        var (publicKeyPem, privateKeyBytes) = generator.GenerateEd25519();
        var privatePem = System.Text.Encoding.ASCII.GetString(privateKeyBytes);
        return (privatePem, publicKeyPem);
    }

    [Fact]
    public async Task Adopt_HappyPath_MatchingTrustRow_passes_drift_guard_and_publishes()
    {
        var tenantId = Guid.NewGuid();
        StubTenant(tenantId);
        _keys.GetActiveByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns((CryptoKey?)null);

        // Mint the canonical PEM via the real validator so we know its sha256,
        // then stub the trust row's hash to match the DERIVED-from-priv pub.
        // 2026-09-10 refactor: the request body only carries the private-key
        // PEM; the public half is derived inside the handler and compared to
        // the trust row by the drift guard.
        var (priv, _) = NewAdoptPair();
        var canonicalPubPem = new Ed25519PEMValidator().DerivePublicKeyPem(priv);
        var canonicalSha256 = Sha256Hex(canonicalPubPem);

        _trust.GetActivePublicKeySha256Async(TenantInstitutionCode, Arg.Any<CancellationToken>())
            .Returns(canonicalSha256);
        _trust.PublishActivePublicKeyAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PublishedTrustKey(ActiveKeyVersion: 1, PublicKeySha256: canonicalSha256)));

        var sut = CreateSut();
        var result = await sut.Handle(
            new GenerateOrAdoptCryptoKeyCommand(tenantId, CryptoKeyMode.Adopt, priv),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        // Drift guard ran.
        await _trust.Received(1).GetActivePublicKeySha256Async(
            TenantInstitutionCode, Arg.Any<CancellationToken>());

        // Auto-publish ran (a refresh of the matching trust row).
        await _trust.Received(1).PublishActivePublicKeyAsync(
            TenantInstitutionCode, "01", TenantInstitutionName, Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        _audit.Entries.Should().Contain(e => e.Action == "crypto_key.adopted");
        _audit.Entries.Should().NotContain(e => e.Action == "crypto_key.adopt.drift_rejected");
        _audit.Entries.Should().NotContain(e => e.Action == "crypto_key.adopt.no_trust_row");
    }

    [Fact]
    public async Task Adopt_NoTrustRow_fails_closed_with_no_trust_row_audit()
    {
        // 2026-09-10 refactor: Adopt is two-step mandatory. If the trust row
        // is missing for the institution, the handler fails closed with
        // 409 InvariantViolation and a crypto_key.adopt.no_trust_row audit
        // row. It does NOT auto-publish — the public-key half must already
        // be in the trust directory (POST /v1/admin/institutions).
        var tenantId = Guid.NewGuid();
        StubTenant(tenantId);
        _keys.GetActiveByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns((CryptoKey?)null);
        _trust.GetActivePublicKeySha256Async(TenantInstitutionCode, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var (priv, _) = NewAdoptPair();
        var sut = CreateSut();

        var result = await sut.Handle(
            new GenerateOrAdoptCryptoKeyCommand(tenantId, CryptoKeyMode.Adopt, priv),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("no trust row");
        result.ErrorMessage.Should().Contain("POST /v1/admin/institutions");

        // No DB row, no vault write, no auto-publish.
        await _keys.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _trust.DidNotReceiveWithAnyArgs().PublishActivePublicKeyAsync(
            default!, default!, default!, default!, default);

        // crypto_key.adopt.no_trust_row MUST fire; the other Adopt actions
        // MUST NOT.
        _audit.Entries.Should().ContainSingle(e => e.Action == "crypto_key.adopt.no_trust_row",
            "Adopt without a trust row must emit exactly one crypto_key.adopt.no_trust_row audit row.");
        _audit.Entries.Should().NotContain(e => e.Action == "crypto_key.adopted");
        _audit.Entries.Should().NotContain(e => e.Action == "crypto_key.adopt.drift_rejected");
    }

    [Fact]
    public async Task Adopt_DriftRejection_writes_audit_row_and_skips_db_and_vault()
    {
        var tenantId = Guid.NewGuid();
        StubTenant(tenantId);
        _keys.GetActiveByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns((CryptoKey?)null);

        // Trust row claims a DIFFERENT public key than the priv will derive.
        _trust.GetActivePublicKeySha256Async(TenantInstitutionCode, Arg.Any<CancellationToken>())
            .Returns("0000000000000000000000000000000000000000000000000000000000000000");

        var (priv, _) = NewAdoptPair();
        var sut = CreateSut();

        var result = await sut.Handle(
            new GenerateOrAdoptCryptoKeyCommand(tenantId, CryptoKeyMode.Adopt, priv),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("trust_store public-key drift");

        // No DB row, no vault write, no auto-publish.
        await _keys.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _trust.DidNotReceiveWithAnyArgs().PublishActivePublicKeyAsync(
            default!, default!, default!, default!, default);

        // crypto_key.adopt.drift_rejected MUST fire; the other Adopt actions
        // MUST NOT.
        _audit.Entries.Should().ContainSingle(e => e.Action == "crypto_key.adopt.drift_rejected");
        _audit.Entries.Should().NotContain(e => e.Action == "crypto_key.adopted");
        _audit.Entries.Should().NotContain(e => e.Action == "crypto_key.adopt.no_trust_row");
    }

    [Fact]
    public async Task Adopt_MalformedPrivateKey_returns_failure_without_audit()
    {
        // 2026-09-10 refactor: there is no longer a separately-supplied
        // public-key PEM to mismatch against the priv. The validator parses
        // the priv alone; a malformed priv throws ArgumentException which
        // the handler catches and maps to 409 InvariantViolation with no
        // audit row (matching the original Generate-failure behaviour).
        var tenantId = Guid.NewGuid();
        StubTenant(tenantId);
        _keys.GetActiveByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns((CryptoKey?)null);

        const string malformedPriv =
            "-----BEGIN PRIVATE KEY-----\nnot-a-real-base64-blob\n-----END PRIVATE KEY-----";

        var sut = CreateSut();
        var result = await sut.Handle(
            new GenerateOrAdoptCryptoKeyCommand(tenantId, CryptoKeyMode.Adopt, malformedPriv),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("did not parse");

        // Validator throws BEFORE we touch drift guard, DB, or vault.
        await _trust.DidNotReceiveWithAnyArgs().GetActivePublicKeySha256Async(default!, default);
        await _trust.DidNotReceiveWithAnyArgs().PublishActivePublicKeyAsync(
            default!, default!, default!, default!, default);
        await _keys.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _uow.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);

        _audit.Entries.Should().NotContain(e =>
            e.Action == "crypto_key.adopted"
            || e.Action == "crypto_key.adopt.drift_rejected"
            || e.Action == "crypto_key.adopt.no_trust_row");
    }

    [Fact]
    public async Task Generate_Unchanged_skips_drift_guard_and_emits_minted_audit()
    {
        var tenantId = Guid.NewGuid();
        StubTenant(tenantId);
        _keys.GetActiveByTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns((CryptoKey?)null);
        _trust.PublishActivePublicKeyAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PublishedTrustKey(ActiveKeyVersion: 1, PublicKeySha256: "abc123")));

        var sut = CreateSut();
        var result = await sut.Handle(
            new GenerateOrAdoptCryptoKeyCommand(tenantId, CryptoKeyMode.Generate),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        // Drift guard is Adopt-only — Generate must not call it.
        await _trust.DidNotReceiveWithAnyArgs().GetActivePublicKeySha256Async(default!, default);

        // Generate emits crypto_key.minted (NOT crypto_key.adopted).
        _audit.Entries.Should().Contain(e => e.Action == "crypto_key.minted");
        _audit.Entries.Should().NotContain(e => e.Action == "crypto_key.adopted");
    }

    private static string Sha256Hex(string ascii)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(ascii);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
