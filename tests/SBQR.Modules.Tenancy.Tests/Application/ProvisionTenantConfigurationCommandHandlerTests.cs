using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.Tenancy.Application.Commands.ProvisionTenantConfiguration;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ProvisionTenantConfigurationCommandHandler"/>.
/// Covers:
/// <list type="bullet">
///   <item>Happy path (Pending tenant): the configuration is minted through the
///         <see cref="ITenantConfigurationProvisioner.ProvisionAsync"/> seam with
///         the tenant's Guid + institution code + the admin's QR capability
///         choice, the one-time secret rides the result, and a single
///         Tenancy-side audit breadcrumb is written whose metadata carries the
///         client_id but NEVER the secret.</item>
///   <item>Happy path (Active tenant): an already-activated tenant may also
///         provision its initial configuration.</item>
///   <item>Capability propagation: the admin's <c>isQrGenerationAllowed</c> /
///         <c>isQrValidationAllowed</c> flags from the command are forwarded
///         to the seam so the new <c>tenant_configurations</c> row starts in
///         the requested state.</item>
///   <item><see cref="ErrorCode.NotFound"/> when the tenant row is missing.</item>
///   <item><see cref="ErrorCode.InvariantViolation"/> for Suspended and
///         Terminated tenants (configurations would be unusable — the token
///         endpoint re-checks admission).</item>
///   <item><see cref="ErrorCode.InvariantViolation"/> when the provisioner
///         reports an existing active configuration (rotation is a separate
///         flow) — surfaced as 409, not a 500.</item>
/// </list>
/// Collaborators are mocked with NSubstitute: these tests pin the
/// orchestration contract, not EF Core or Argon2id.
/// </summary>
public sealed class ProvisionTenantConfigurationCommandHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ITenantConfigurationProvisioner _configurations =
        Substitute.For<ITenantConfigurationProvisioner>();
    private readonly IActorProvider _actor = Substitute.For<IActorProvider>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();

    private ProvisionTenantConfigurationCommandHandler CreateSut() => new(
        _tenants, _configurations, _actor, _audit);

    private static ProvisionedConfiguration NewConfiguration(Guid tenantId) => new(
        CredentialId: Guid.NewGuid(),
        ClientId: "010101-7c1b4d88",
        ClientSecret: "s3cret-returned-once",
        ExpiresAt: DateTimeOffset.UtcNow.AddYears(1));

    [Fact]
    public async Task Handle_should_provision_configuration_for_pending_tenant()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.ClearDomainEvents();

        var configuration = NewConfiguration(tenant.Id.Value);
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        // Defaults on the command are (true, true) — pin the seam call with
        // explicit values so a future change to the defaults doesn't silently
        // break the seam contract.
        _configurations
            .ProvisionAsync(
                tenant.Id.Value,
                "010101",
                true,
                true,
                Arg.Any<CancellationToken>())
            .Returns(configuration);
        _actor.CurrentActor().Returns("platform-staff");

        var sut = CreateSut();
        var result = await sut.Handle(
            new ProvisionTenantConfigurationCommand(tenant.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        // The one-time plaintext secret rides the result verbatim.
        result.Value.TenantId.Should().Be(tenant.Id);
        result.Value.CredentialId.Should().Be(configuration.CredentialId);
        result.Value.ClientId.Should().Be(configuration.ClientId);
        result.Value.ClientSecret.Should().Be(configuration.ClientSecret);
        result.Value.ExpiresAt.Should().Be(configuration.ExpiresAt);

        // The seam fired with the tenant's Guid + the institution code read
        // from the tenant row (not caller-supplied) + the admin's QR
        // capability choice from the command body.
        await _configurations.Received(1).ProvisionAsync(
            Arg.Is<Guid>(g => g == tenant.Id.Value),
            "010101",
            Arg.Is<bool>(b => b),
            Arg.Is<bool>(b => b),
            Arg.Any<CancellationToken>());

        // One Tenancy-side breadcrumb, attributed to the acting admin, with
        // the client_id but never the secret.
        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "tenant.configuration.provisioned"
                && e.ActorId == "platform-staff"
                && e.ResourceType == "Tenant"
                && e.ResourceId == tenant.Id.Value.ToString()
                && e.TenantId == tenant.Id.Value
                && e.Metadata!.Contains("\"client_id\":\"010101-7c1b4d88\"")
                && !e.Metadata.Contains(configuration.ClientSecret)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_propagate_admin_choice_of_qr_capabilities_to_seam()
    {
        // The admin opts the tenant into QR-validation only; the handler must
        // forward (false, true) into the seam so the new tenant_configurations
        // row authorises verification but rejects generation from the start.
        var tenant = Tenant.Register("Nagad", "020202");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _configurations
            .ProvisionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(NewConfiguration(tenant.Id.Value));

        var sut = CreateSut();
        var result = await sut.Handle(
            new ProvisionTenantConfigurationCommand(
                tenant.Id,
                IsQrGenerationAllowed: false,
                IsQrValidationAllowed: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await _configurations.Received(1).ProvisionAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Is<bool>(b => b == false),
            Arg.Is<bool>(b => b == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_provision_configuration_for_active_tenant()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Activate("platform-staff");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _configurations
            .ProvisionAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(NewConfiguration(tenant.Id.Value));
        _actor.CurrentActor().Returns("platform-staff");

        var sut = CreateSut();
        var result = await sut.Handle(
            new ProvisionTenantConfigurationCommand(tenant.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await _configurations.Received(1).ProvisionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_return_NotFound_when_tenant_is_missing()
    {
        var missing = new TenantId(Guid.NewGuid());
        _tenants.GetByIdAsync(missing, Arg.Any<CancellationToken>()).ReturnsNull();

        var sut = CreateSut();
        var result = await sut.Handle(
            new ProvisionTenantConfigurationCommand(missing),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);
        result.ErrorMessage.Should().Contain(missing.Value.ToString("D"));

        // Nothing minted, nothing audited.
        await _configurations.DidNotReceive().ProvisionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]   // Suspended
    [InlineData(false)]  // Terminated
    public async Task Handle_should_return_InvariantViolation_for_suspended_or_terminated_tenant(bool suspended)
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        if (suspended)
        {
            tenant.Suspend("platform-staff", "KYC review");
        }
        else
        {
            tenant.Deactivate("platform-staff", "offboarded");
        }

        tenant.ClearDomainEvents();
        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);

        var sut = CreateSut();
        var result = await sut.Handle(
            new ProvisionTenantConfigurationCommand(tenant.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain(suspended ? "Suspended" : "Terminated");

        await _configurations.DidNotReceive().ProvisionAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_active_configuration_already_exists()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _configurations
            .When(c => c.ProvisionAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()))
            .Throw(new InvalidOperationException(
                $"Tenant {tenant.Id.Value} already has an active TenantConfiguration; " +
                "use the rotation flow instead of re-provisioning."));

        var sut = CreateSut();
        var result = await sut.Handle(
            new ProvisionTenantConfigurationCommand(tenant.Id),
            CancellationToken.None);

        // The provisioner's single-active-configuration guard surfaces as a 409,
        // not an unhandled 500.
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("rotation flow");

        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }
}
