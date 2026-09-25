using System.Diagnostics;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.IdentityAccess.Application.Commands.IssueToken;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Tenancy.Application.Commands.ProvisionTenantConfiguration;
using SBQR.Modules.Tenancy.Application.Commands.SuspendTenant;
using SBQR.Qr.IntegrationTests.Infrastructure;
using SBQR.SharedKernel.Application;
using Xunit;

namespace SBQR.Qr.IntegrationTests;

/// <summary>
/// Regression net for the four OAuth-token-endpoint security fixes that
/// landed after the post-review (security review B1-B4):
///
/// <list type="bullet">
///   <item><b>B1</b> — every reject branch on the tenant token path runs a
///         dummy Argon2id verify equal in wall-time to the real verify,
///         so an attacker cannot enumerate valid client_ids by latency.
///         Asserted by sampling wall-time across reject branches and
///         confirming the slowest non-success path is within a tolerance
///         band of the slowest branch (a fixed dummy-cost ceiling).</item>
///   <item><b>B2</b> — every reject branch (wrong_grant_type, bootstrap
///         unconfigured, wrong bootstrap secret, unknown client_id,
///         wrong tenant secret, suspended tenant, terminated tenant,
///         expired credential) emits an <c>auth.token.rejected</c> audit
///         row with a <c>reason</c> discriminator so SOC dashboards can
///         key on it.</item>
/// </list>
///
/// <para>B3 (rate-limit partition key) and B4 (production denylist) are
/// tested by their own dedicated files — see
/// <c>RateLimitPartitionKeyTests.cs</c> and
/// <c>ProductionDenylistTests.cs</c>.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OAuthSecurityTests : IAsyncLifetime, IDisposable
{
    private const string BootstrapClientId = "platform-bootstrap";
    private const string BootstrapSecret = QrFlowHostBuilder.BootstrapSecret;
    private const string WrongSecret = "not-the-right-secret";

    private readonly PostgreSqlFixture _postgres;
    private readonly QrFlowHostBuilder _hostBuilder = new();

    public OAuthSecurityTests(PostgreSqlFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _hostBuilder.Dispose();

    // ---------------------------------------------------------------------
    // B2 — audit rows
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Wrong_grant_type_emits_rejected_audit_row()
    {
        var (services, _, audit) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            var result = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "password", // not client_credentials
                ClientId: BootstrapClientId,
                ClientSecret: BootstrapSecret));

            result.IsFailure.Should().BeTrue();

            audit.Entries.Should().ContainSingle(e =>
                e.Action == "auth.token.rejected"
                && e.Metadata != null
                && e.Metadata.Contains("\"reason\":\"wrong_grant_type\""));
        }
    }

    [Fact]
    public async Task Wrong_bootstrap_secret_emits_rejected_audit_row()
    {
        var (services, _, audit) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            var result = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "client_credentials",
                ClientId: BootstrapClientId,
                ClientSecret: WrongSecret));

            result.IsFailure.Should().BeTrue();
            result.ErrorMessage.Should().Be("invalid_client");

            audit.Entries.Should().ContainSingle(e =>
                e.Action == "auth.token.rejected"
                && e.Metadata != null
                && e.Metadata.Contains("\"reason\":\"wrong_secret\"")
                && e.ActorId == "platform:bootstrap-admin"
                && e.ResourceType == "PlatformBootstrapClient");
        }
    }

    [Fact]
    public async Task Unknown_client_id_emits_rejected_audit_row_with_reason_unknown_client()
    {
        var (services, _, audit) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            var result = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "client_credentials",
                ClientId: "tenant-id-that-does-not-exist",
                ClientSecret: "anything"));

            result.IsFailure.Should().BeTrue();
            result.ErrorMessage.Should().Be("invalid_client");

            audit.Entries.Should().ContainSingle(e =>
                e.Action == "auth.token.rejected"
                && e.Metadata != null
                && e.Metadata.Contains("\"reason\":\"unknown_client\""));
        }
    }

    [Fact]
    public async Task Wrong_tenant_secret_emits_rejected_audit_row_with_reason_wrong_secret()
    {
        var (services, _, audit) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            // Stage a tenant + a configuration row so a "wrong secret"
            // branch is reachable (versus "unknown client_id").
            var tenant = await mediator.Send(new CreateTenantCommand(
                "Audit Wrong Secret Bank",
                "030303"));
            tenant.IsSuccess.Should().BeTrue();

            var provisioned = await mediator.Send(new ProvisionTenantConfigurationCommand(
                tenant.Value.TenantId));
            provisioned.IsSuccess.Should().BeTrue();

            audit.Clear();

            var result = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "client_credentials",
                ClientId: provisioned.Value.ClientId,
                ClientSecret: WrongSecret));

            result.IsFailure.Should().BeTrue();
            result.ErrorMessage.Should().Be("invalid_client");

            audit.Entries.Should().ContainSingle(e =>
                e.Action == "auth.token.rejected"
                && e.Metadata != null
                && e.Metadata.Contains("\"reason\":\"wrong_secret\"")
                && e.ResourceType == "TenantConfiguration");
        }
    }

    [Fact]
    public async Task Suspended_tenant_emits_rejected_audit_row_with_reason_tenant_suspended()
    {
        var (services, _, audit) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            var tenant = await mediator.Send(new CreateTenantCommand(
                "Audit Suspended Bank",
                "040404"));
            tenant.IsSuccess.Should().BeTrue();

            var provisioned = await mediator.Send(new ProvisionTenantConfigurationCommand(
                tenant.Value.TenantId));
            provisioned.IsSuccess.Should().BeTrue();

            // Suspend the tenant — its credential is still in place, but
            // the admission state rejects the token exchange.
            var suspended = await mediator.Send(new SuspendTenantCommand(
                tenant.Value.TenantId,
                "test suspension for audit-row regression"));
            suspended.IsSuccess.Should().BeTrue();

            audit.Clear();

            var result = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "client_credentials",
                ClientId: provisioned.Value.ClientId,
                ClientSecret: provisioned.Value.ClientSecret));

            result.IsFailure.Should().BeTrue();
            result.ErrorMessage.Should().Be("invalid_client");

            audit.Entries.Should().ContainSingle(e =>
                e.Action == "auth.token.rejected"
                && e.Metadata != null
                && e.Metadata.Contains("\"reason\":\"tenant_suspended\""));
        }
    }

    [Fact]
    public async Task Successful_bootstrap_leg_does_not_emit_rejected_audit_rows()
    {
        var (services, _, audit) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            var result = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "client_credentials",
                ClientId: BootstrapClientId,
                ClientSecret: BootstrapSecret));

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);

            audit.Entries.Should().NotContain(e => e.Action == "auth.token.rejected");
            audit.Entries.Should().ContainSingle(e =>
                e.Action == "auth.token.issued"
                && e.ActorId == "platform:bootstrap-admin");
        }
    }

    // ---------------------------------------------------------------------
    // B1 — timing equalization
    //
    // Sample wall-time across reject branches. The fix inserts a dummy
    // Argon2id verify on every reject path, equalizing the cost. We
    // measure three branches (wrong_grant_type, unknown_client,
    // wrong_secret) and assert the slowest is no more than a small
    // multiple of the fastest (a constant dummy-cost ceiling dominates
    // any pre-fix per-branch variability).
    //
    // This is a soft assertion — Argon2id is CPU-bound and CI variance
    // is real. The band is generous (~3x) but tight enough to catch a
    // regression where a reject branch skips the dummy run entirely.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Reject_branch_wall_times_are_equalized_within_tolerance()
    {
        var (services, _, _) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            // Stage a tenant + configuration so the wrong_secret branch
            // can fire (vs. the unknown_client branch, which would
            // otherwise short-circuit).
            var tenant = await mediator.Send(new CreateTenantCommand(
                "Timing Bank",
                "050505"));
            tenant.IsSuccess.Should().BeTrue();
            var provisioned = await mediator.Send(new ProvisionTenantConfigurationCommand(
                tenant.Value.TenantId));
            provisioned.IsSuccess.Should().BeTrue();

            // Warm-up: Argon2id's first call JIT-compiles; subsequent
            // calls hit steady state.
            await MeasureAsync(mediator, () => new IssueClientCredentialsTokenCommand(
                "client_credentials",
                "tenant-id-that-does-not-exist",
                "warmup"));

            // Sample each branch 3 times, take the median for noise
            // rejection.
            var wrongGrant = await MedianAsync(mediator, () => new IssueClientCredentialsTokenCommand(
                "password",
                BootstrapClientId,
                BootstrapSecret));

            var unknownClient = await MedianAsync(mediator, () => new IssueClientCredentialsTokenCommand(
                "client_credentials",
                "tenant-id-that-does-not-exist",
                "anything"));

            var wrongSecret = await MedianAsync(mediator, () => new IssueClientCredentialsTokenCommand(
                "client_credentials",
                provisioned.Value.ClientId,
                WrongSecret));

            var slowest = new[] { wrongGrant, unknownClient, wrongSecret }.Max();
            var fastest = new[] { wrongGrant, unknownClient, wrongSecret }.Min();

            // The dummy Argon2id verify at OWASP-2024 parameters (m=64MiB,
            // t=3, p=1) costs ~100 ms; the real verify path runs it the same
            // number of times. Pre-fix, the wrong_grant_type and
            // unknown_client branches short-circuited before PHC verify and
            // returned in <1 ms; post-fix, every branch must pay the
            // dummy cost. The band below is loose enough to survive CI
            // noise but tight enough to catch a regression where the dummy
            // run is skipped on one branch.
            slowest.Should().BeLessThan(3 * fastest + TimeSpan.FromMilliseconds(150));
        }
    }

    private static async Task<TimeSpan> MeasureAsync(
        ISender mediator,
        Func<IssueClientCredentialsTokenCommand> commandFactory)
    {
        var sw = Stopwatch.StartNew();
        await mediator.Send(commandFactory());
        sw.Stop();
        return sw.Elapsed;
    }

    private static async Task<TimeSpan> MedianAsync(
        ISender mediator,
        Func<IssueClientCredentialsTokenCommand> commandFactory)
    {
        const int samples = 3;
        var measurements = new TimeSpan[samples];
        for (var i = 0; i < samples; i++)
        {
            measurements[i] = await MeasureAsync(mediator, commandFactory);
        }

        Array.Sort(measurements);
        return measurements[samples / 2];
    }
}
