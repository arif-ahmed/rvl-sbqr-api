using System.Text;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;
using SBQR.Modules.InstitutionTrust.Application.Commands;
using SBQR.Modules.KeyCustody.Application.Commands.GenerateOrAdoptCryptoKey;
using SBQR.Modules.QrGeneration.Application.Commands;
using SBQR.Modules.QrGeneration.Application.Commands.GenerateDynamicQr;
using SBQR.Modules.QrGeneration.Application.Commands.GenerateStaticQr;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Verification.Application.Commands;
using SBQR.Qr.IntegrationTests.Infrastructure;
using SBQR.SharedKernel.QrCodec;
using Xunit;

namespace SBQR.Qr.IntegrationTests;

/// <summary>
/// The Sep-17 demo path end-to-end, on real PostgreSQL and real Ed25519/AES
/// crypto: tenant registration mints a custodied key in the encrypted file
/// vault; KeyCustody auto-publishes the public key into the InstitutionTrust
/// trust directory; issuance signs through KeyCustody; verification resolves
/// ALL issuers through the single BB trust store per spec Annex B
/// (tenant keys published into institution_keys with source = 'LOCAL');
/// every rejection path stays fail-closed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class QrRoundTripTests : IAsyncLifetime, IDisposable
{
    private readonly PostgreSqlFixture _postgres;
    private readonly QrFlowHostBuilder _hostBuilder = new();

    public QrRoundTripTests(PostgreSqlFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _hostBuilder.Dispose();

    [Fact]
    public async Task Register_issue_and_verify_the_full_round_trip()
    {
        var (services, currentTenant, audit) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            // 1. Register a tenant with institution code 031008.
            var tenant = await mediator.Send(new CreateTenantCommand(
                InstitutionName: "ACME Bank",
                InstitutionCode: "031008"));
            tenant.IsSuccess.Should().BeTrue(tenant.ErrorMessage);
            var tenantId = tenant.Value.TenantId.Value;

            // 2. Mint the tenant's first ACTIVE signing key into the
            //    encrypted file vault — the issuance pipeline requires it.
            var minted = await mediator.Send(new GenerateOrAdoptCryptoKeyCommand(
                TenantId: tenantId,
                Mode: CryptoKeyMode.Generate));
            minted.IsSuccess.Should().BeTrue(minted.ErrorMessage);

            // 3. Issue a signed QR as that tenant.
            currentTenant.TenantId = tenantId;
            var generated = await mediator.Send(new GenerateStaticQrCommand(
                RecipientName: "Arif Mahmood",
                RecipientCity: "Dhaka",
                RecipientPan: "01711111111"));
            generated.IsSuccess.Should().BeTrue(generated.ErrorMessage);
            generated.Value.QrPayload.Should().StartWith("0002");

            // 4. Verify our own QR — trust-directory path (spec Annex B:
            //    ALL issuers resolve through institution_keys, including our
            //    own tenants whose keys are auto-published by KeyCustody).
            var own = await mediator.Send(NewValidateCommand(generated.Value.QrPayload));
            own.Verdict.Should().Be(QrVerdict.VALID);
            own.TrustSource.Should().Be(QrTrustSource.TRUST_DIRECTORY);
            own.InstitutionCode.Should().Be("031008");
            own.RecipientName.Should().Be("Arif Mahmood");
            own.RecipientPan.Should().Be("01711111111");

            // 5. Seed an external institution into the trust directory.
            //    The institution's display name + Annex A Institution Type
            //    are denormalised onto institution_keys (NOT NULL on the
            //    row) so the verifier can resolve the issuer's identity
            //    without joining public.tenants.
            var (externalPublicPem, externalPrivateKey) = GenerateExternalKey();
            var seeded = await mediator.Send(new UpsertInstitutionCommand(
                InstitutionCode: "000909",
                InstituteType: "00",
                InstitutionName: "External Bank",
                PublicKeyPem: externalPublicPem));
            seeded.IsSuccess.Should().BeTrue(seeded.ErrorMessage);

            // 6. Build + sign a QR as that external institution would
            //    (private key held outside the platform) and verify —
            //    trust-directory path (spec Annex B).
            var externalQr = BuildExternallySignedQr(externalPrivateKey, "00", "0909");
            var external = await mediator.Send(NewValidateCommand(externalQr));
            external.Verdict.Should().Be(QrVerdict.VALID);
            external.TrustSource.Should().Be(QrTrustSource.TRUST_DIRECTORY);
            external.InstitutionCode.Should().Be("000909");

            // 7. Fail-closed: same external QR signed by the WRONG key —
            //    structurally perfect, CRC valid, signature does not verify.
            var (_, impostorKey) = GenerateExternalKey();
            var impostorQr = BuildExternallySignedQr(impostorKey, "00", "0909");
            var rejected = await mediator.Send(NewValidateCommand(impostorQr));
            rejected.Verdict.Should().Be(QrVerdict.INVALID_SIGNATURE);
            rejected.ReasonCode.Should().Be("SIGNATURE_MISMATCH");

            // 8. Fail-closed: structurally corrupted payload.
            var corrupted = generated.Value.QrPayload[..^1]
                + (generated.Value.QrPayload[^1] == '0' ? '1' : '0');
            var structural = await mediator.Send(NewValidateCommand(corrupted));
            structural.Verdict.Should().Be(QrVerdict.STRUCTURAL_INVALID);
            structural.ReasonCode.Should().Be("CRC_MISMATCH");

            // 9. Fail-closed: unknown issuer — not our tenant, not in the
            //    directory.
            var (_, unknownKey) = GenerateExternalKey();
            var unknownQr = BuildExternallySignedQr(unknownKey, "04", "7777");
            var unknown = await mediator.Send(NewValidateCommand(unknownQr));
            unknown.Verdict.Should().Be(QrVerdict.KEY_NOT_FOUND);

            // 10. C6 replay guard: the SAME (tenant, request id) is
            //     single-use — re-sending the command verbatim is rejected
            //     without a second outcome row.
            var replayed = await mediator.Send(NewValidateCommand(generated.Value.QrPayload));
            replayed.Verdict.Should().Be(QrVerdict.VALID);
            var replay = NewValidateCommand(generated.Value.QrPayload);
            var first = await mediator.Send(replay);
            first.Verdict.Should().Be(QrVerdict.VALID);
            var second = await mediator.Send(replay);
            second.Verdict.Should().Be(QrVerdict.REQUEST_REPLAYED);
            second.ReasonCode.Should().Be("REPLAY_DETECTED");

            // Every verification attempt recorded an outcome row + audit.
            audit.Entries.Should().Contain(e => e.Action == "qr.generated");
            audit.Entries.Should().Contain(e => e.Action == "qr.validated");
        }
    }

    /// <summary>
    /// Static and dynamic QR generation both work end-to-end on real
    /// Postgres + real Ed25519 signing: the Point of Initiation Method
    /// (Tag 01) and the persisted <c>QrType</c> reflect the requested mode,
    /// and the resulting QR still self-verifies as VALID.
    /// </summary>
    [Theory]
    [InlineData(false, "11", "STATIC")]
    [InlineData(true, "12", "DYNAMIC")]
    public async Task Generate_and_verify_both_static_and_dynamic_qr(
        bool isDynamic, string expectedPointOfInitiation, string expectedQrType)
    {
        var (services, currentTenant, _) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            var tenant = await mediator.Send(new CreateTenantCommand(
                InstitutionName: "ACME Bank",
                InstitutionCode: "031008"));
            tenant.IsSuccess.Should().BeTrue(tenant.ErrorMessage);
            currentTenant.TenantId = tenant.Value.TenantId.Value;

            // Mint the tenant's first ACTIVE signing key — issuance requires it.
            var minted = await mediator.Send(new GenerateOrAdoptCryptoKeyCommand(
                TenantId: tenant.Value.TenantId.Value,
                Mode: CryptoKeyMode.Generate));
            minted.IsSuccess.Should().BeTrue(minted.ErrorMessage);

            var generated = isDynamic
                ? await mediator.Send(new GenerateDynamicQrCommand(
                    TransactionAmount: "100.00",
                    RecipientName: "Arif Mahmood",
                    RecipientCity: "Dhaka",
                    RecipientPan: "01711111111"))
                : await mediator.Send(new GenerateStaticQrCommand(
                    RecipientName: "Arif Mahmood",
                    RecipientCity: "Dhaka",
                    RecipientPan: "01711111111"));
            generated.IsSuccess.Should().BeTrue(generated.ErrorMessage);
            generated.Value.QrType.Should().Be(expectedQrType);

            var parsed = QrPayloadParser.Parse(generated.Value.QrPayload);
            parsed.IsSuccess.Should().BeTrue(string.Join("; ", parsed.Errors.Select(e => e.Message)));
            parsed.Payload.PointOfInitiation.Should().Be(expectedPointOfInitiation);

            var verified = await mediator.Send(NewValidateCommand(generated.Value.QrPayload));
            verified.Verdict.Should().Be(QrVerdict.VALID);
        }
    }

    /// <summary>
    /// Builds a verification command with a fresh single-use request id and
    /// current UTC timestamp — the C6 replay-guard contract every caller
    /// must satisfy.
    /// </summary>
    private static ValidateQrCommand NewValidateCommand(string qrPayload) =>
        new(qrPayload, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);

    /// <summary>Generates the keypair an external institution would own (public PEM + private params).</summary>
    private static (string PublicPem, Ed25519PrivateKeyParameters PrivateKey) GenerateExternalKey()
    {
        var seed = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        var publicKey = privateKey.GeneratePublicKey();

        using var writer = new StringWriter();
        using (var pemWriter = new PemWriter(writer))
        {
            pemWriter.WriteObject(publicKey);
        }

        return (writer.ToString(), privateKey);
    }

    /// <summary>
    /// Builds a QR as the external institution's own system would: codec
    /// build → Ed25519 sign the canonical signature payload → finalize
    /// (Tags 80/81 + CRC last). For the impostor case the signing key does
    /// not match the directory key.
    /// </summary>
    private static string BuildExternallySignedQr(
        Ed25519PrivateKeyParameters privateKey,
        string institutionType,
        string institutionId)
    {
        var unsigned = P2pQrBuilder.Build(new P2pQrRequest(
            IsDynamic: false,
            InstitutionType: institutionType,
            InstitutionId: institutionId,
            RecipientPan: "01511000111",
            RecipientName: "External User",
            RecipientCity: "Sylhet"));
        unsigned.IsSuccess.Should().BeTrue(string.Join("; ", unsigned.Errors.Select(e => e.Message)));

        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        var payload = unsigned.Value.SignaturePayloadBytes;
        signer.BlockUpdate(payload, 0, payload.Length);

        return QrPayloadFinalizer.Finalize(
            unsigned.Value,
            Convert.ToBase64String(signer.GenerateSignature()));
    }
}
