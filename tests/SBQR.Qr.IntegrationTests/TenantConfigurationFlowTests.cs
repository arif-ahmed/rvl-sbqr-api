using System.Text.Json;
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
/// The institute-onboarding configuration line end-to-end, on real PostgreSQL and
/// the real module graph (Tenancy + IdentityAccess): platform bootstrap token
/// → register tenant → provision FI client configuration → tenant exchanges
/// them at the OAuth 2.1 token endpoint → suspension cuts the tenant off.
/// This is the regression net for the flow the dedicated configuration endpoint
/// (<c>POST /admin/tenants/{id}/tenant-configuration</c>) completes — before it
/// shipped, the provisioner was registered in DI but unreachable, so a
/// tenant could never authenticate.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantConfigurationFlowTests : IAsyncLifetime, IDisposable
{
    private const string BootstrapClientId = "platform-bootstrap";
    private const string BootstrapSecret = "e2e-bootstrap-secret";

    private readonly PostgreSqlFixture _postgres;
    private readonly QrFlowHostBuilder _hostBuilder = new();

    public TenantConfigurationFlowTests(PostgreSqlFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _hostBuilder.Dispose();

    [Fact]
    public async Task Bootstrap_register_provision_and_tenant_token_flow()
    {
        var (services, _, audit) = _hostBuilder.Build(_postgres);
        await using (services)
        await using (var scope = services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();

            // 1. Platform bootstrap leg: exchange the config-held client
            //    credential for an admin-scoped token.
            var bootstrap = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "client_credentials",
                ClientId: BootstrapClientId,
                ClientSecret: BootstrapSecret));
            bootstrap.IsSuccess.Should().BeTrue(bootstrap.ErrorMessage);
            var bootstrapClaims = DecodeJwtPayload(bootstrap.Value.AccessToken);
            bootstrapClaims.GetProperty("sub").GetString().Should().Be("platform:bootstrap-admin");
            ScopeValues(bootstrapClaims).Should().Contain("admin").And.NotContain("qr:generate");

            // 2. Register a tenant — ships in Pending, no configurations.
            var tenant = await mediator.Send(new CreateTenantCommand(
                InstitutionName: "Configuration Flow Bank",
                InstitutionCode: "020202"));
            tenant.IsSuccess.Should().BeTrue(tenant.ErrorMessage);
            var tenantId = tenant.Value.TenantId.Value;

            // 3. Provision the initial FI client configuration. A Pending tenant
            //    may provision — registration must be exercisable end-to-end
            //    before BB activation.
            var provisioned = await mediator.Send(new ProvisionTenantConfigurationCommand(
                TenantId: tenant.Value.TenantId));
            provisioned.IsSuccess.Should().BeTrue(provisioned.ErrorMessage);
            provisioned.Value.ClientId.Should().StartWith("020202-");
            provisioned.Value.ClientSecret.Should().NotBeNullOrWhiteSpace();

            // The one-time secret never reaches any audit row.
            audit.Entries.Should().NotContain(e =>
                (e.Metadata ?? string.Empty).Contains(provisioned.Value.ClientSecret));
            audit.Entries.Should().Contain(e => e.Action == "auth.client_credentials.issued");
            audit.Entries.Should().Contain(e => e.Action == "tenant.configuration.provisioned");

            // 4. Tenant leg: exchange the provisioned configuration for a
            //    tenant-scoped token.
            var tenantToken = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "client_credentials",
                ClientId: provisioned.Value.ClientId,
                ClientSecret: provisioned.Value.ClientSecret));
            tenantToken.IsSuccess.Should().BeTrue(tenantToken.ErrorMessage);
            var tenantClaims = DecodeJwtPayload(tenantToken.Value.AccessToken);
            tenantClaims.GetProperty("sub").GetString()
                .Should().Be($"client:{provisioned.Value.ClientId}");
            tenantClaims.GetProperty("tenant_id").GetString().Should().Be(tenantId.ToString("D"));
            var tenantScopes = ScopeValues(tenantClaims);
            tenantScopes.Should().BeEquivalentTo("qr:generate", "qr:validate");
            tenantScopes.Should().NotContain("admin", "a tenant configuration must never mint admin scope");

            // 5. Fail-closed: wrong secret → invalid_client, indistinguishable
            //    from an unknown client_id.
            var wrongSecret = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "client_credentials",
                ClientId: provisioned.Value.ClientId,
                ClientSecret: "not-the-secret"));
            wrongSecret.IsFailure.Should().BeTrue();
            wrongSecret.ErrorMessage.Should().Be("invalid_client");

            // 6. Re-provisioning while the active configuration exists is
            //    refused — rotation is a separate flow.
            var reprovision = await mediator.Send(new ProvisionTenantConfigurationCommand(
                TenantId: tenant.Value.TenantId));
            reprovision.IsFailure.Should().BeTrue();
            reprovision.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
            reprovision.ErrorMessage.Should().Contain("rotation flow");

            // 7. Suspension cuts the tenant off: the cascade suspends the
            //    configuration row AND the mint-time admission re-check rejects
            //    the tenant even mid-flight.
            var suspended = await mediator.Send(new SuspendTenantCommand(
                TenantId: tenant.Value.TenantId,
                Reason: "cut-off check"));
            suspended.IsSuccess.Should().BeTrue(suspended.ErrorMessage);

            var afterSuspend = await mediator.Send(new IssueClientCredentialsTokenCommand(
                GrantType: "client_credentials",
                ClientId: provisioned.Value.ClientId,
                ClientSecret: provisioned.Value.ClientSecret));
            afterSuspend.IsFailure.Should().BeTrue();
            afterSuspend.ErrorMessage.Should().Be("invalid_client");

            // And a suspended tenant cannot provision a fresh configuration
            // either (it could never mint a usable token).
            var provisionWhileSuspended = await mediator.Send(
                new ProvisionTenantConfigurationCommand(TenantId: tenant.Value.TenantId));
            provisionWhileSuspended.IsFailure.Should().BeTrue();
            provisionWhileSuspended.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        }
    }

    /// <summary>Decode the JWT payload segment without pulling a JWT library in.</summary>
    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3, "a JWS compact serialization has three segments");
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = (payload.Length % 4) switch
        {
            2 => payload + "==",
            3 => payload + "=",
            _ => payload,
        };
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement;
    }

    /// <summary>
    /// The issuer writes scopes as a JSON array of <c>scope</c> values;
    /// normalize to strings so assertions read the same shape for one
    /// (bootstrap) or many (tenant) granted scopes.
    /// </summary>
    private static List<string> ScopeValues(JsonElement payload)
    {
        var scope = payload.GetProperty("scope");
        return scope.ValueKind == JsonValueKind.Array
            ? scope.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToList()
            : [(scope.GetString() ?? string.Empty)];
    }
}
