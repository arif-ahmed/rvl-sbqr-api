using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Tenancy.Application.Commands.RegisterTenantApplication;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;
using SBQR.Tenancy.IntegrationTests.Infrastructure;
using Xunit;

namespace SBQR.Tenancy.IntegrationTests.TenantApplications;

/// <summary>
/// End-to-end coverage for the <c>POST /v1/admin/tenants/{tenantId}/applications</c>
/// flow, against a real PostgreSQL 16-alpine container.
///
/// <para>
/// The contract pinned by these tests is the <b>duplicate-application rule</b>
/// (FR-AUTH-002 / FR-AUTH-003 operational contract): a tenant may register at
/// most one row per <c>(platform, package_id)</c> pair. The same
/// <c>package_id</c> is legal on a second platform — e.g.
/// <c>com.dhakabank.consumer</c> as both an ANDROID row and an IOS row, one
/// per store — but never twice on the same platform. The constraint is
/// enforced by <c>ix_tenant_applications_platform_package_id</c> UNIQUE in
/// migration <c>008_tenant_applications.sql</c>; the EF layer translates the
/// 23505 violation into <see cref="ErrorCode.InvariantViolation"/> which the
/// controller maps to <c>409 Conflict</c>.
/// </para>
///
/// <para>
/// <see cref="ErrorCode.InvariantViolation"/> for the duplicate case is what
/// the production controller returns — the handler maps PG SQLSTATE 23505
/// with the offending (platform, package_id) into the message body. We assert
/// on the error code here (the controller-level message wording is verified
/// by the unit-test layer).
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RegisterTenantApplicationTests : IAsyncLifetime
{
    private const string InstitutionName = "Dhaka Bank PLC";
    private const string InstitutionCode = "030303";

    private readonly PostgreSqlFixture _pg;
    private ServiceProvider _sp = default!;

    public RegisterTenantApplicationTests(PostgreSqlFixture pg)
    {
        _pg = pg ?? throw new ArgumentNullException(nameof(pg));
    }

    public async Task InitializeAsync()
    {
        await _pg.ResetAsync();
        (var sp, _) = TenancyHostBuilder.BuildForPostgres(_pg);
        _sp = sp;
    }

    public async Task DisposeAsync()
    {
        if (_sp is not null)
        {
            await _sp.DisposeAsync();
        }
    }

    /// <summary>Helper: register a fresh tenant so each test starts from a known state.</summary>
    private async Task<TenantId> CreateTenantAsync()
    {
        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = await mediator.Send(
            new CreateTenantCommand(InstitutionName, InstitutionCode));
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        return result.Value.TenantId;
    }

    [Fact]
    public async Task Registers_application_end_to_end()
    {
        var tenantId = await CreateTenantAsync();
        const string PackageId = "com.dhakabank.consumer";

        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = await mediator.Send(
            new RegisterTenantApplicationCommand(
                TenantId: tenantId,
                Platform: TenantApplicationPlatform.Android,
                PackageId: PackageId));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.Platform.Should().Be(TenantApplicationPlatform.Android);
        result.Value.PackageId.Should().Be(PackageId);

        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var apps = await db.TenantApplications.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .ToListAsync();

        apps.Should().HaveCount(1);
        apps[0].Status.Should().Be(TenantApplicationStatus.Active);
        apps[0].IsActive.Should().BeTrue();
        apps[0].Platform.Should().Be(TenantApplicationPlatform.Android);
        apps[0].PackageId.Should().Be(PackageId);
        apps[0].CreatedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
        apps[0].CreatedBy.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Same_package_id_on_same_platform_is_rejected_as_409()
    {
        // The operational rule: at most one row per (platform, package_id).
        // Registering the same package_id twice on ANDROID for the same tenant
        // must surface as InvariantViolation (409). The DB enforces this via
        // ix_tenant_applications_platform_package_id UNIQUE; the handler's
        // 23505 translator surfaces it to the controller.
        var tenantId = await CreateTenantAsync();
        const string PackageId = "com.dhakabank.consumer";

        await using (var firstScope = _sp.CreateAsyncScope())
        {
            var mediator = firstScope.ServiceProvider.GetRequiredService<IMediator>();
            var first = await mediator.Send(
                new RegisterTenantApplicationCommand(
                    TenantId: tenantId,
                    Platform: TenantApplicationPlatform.Android,
                    PackageId: PackageId));
            first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        }

        await using (var dupScope = _sp.CreateAsyncScope())
        {
            var mediator = dupScope.ServiceProvider.GetRequiredService<IMediator>();
            var duplicate = await mediator.Send(
                new RegisterTenantApplicationCommand(
                    TenantId: tenantId,
                    Platform: TenantApplicationPlatform.Android,
                    PackageId: PackageId));

            duplicate.IsFailure.Should().BeTrue();
            duplicate.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
            duplicate.ErrorMessage.Should().Contain(PackageId);
        }

        await using var verifyScope = _sp.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var apps = await db.TenantApplications.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .ToListAsync();
        apps.Should().HaveCount(1, "the second insert must have been blocked by the UNIQUE index");
    }

    [Fact]
    public async Task Same_package_id_on_different_platforms_succeeds_two_rows()
    {
        // The FR-AUTH-002 §5.6 operational contract: the same reverse-DNS string
        // on ANDROID and IOS for one tenant (the common case where an FI ships
        // one app on both stores with the same applicationId/bundleId) is
        // LEGAL — that's two rows.
        var tenantId = await CreateTenantAsync();
        const string PackageId = "com.dhakabank.consumer";

        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var android = await mediator.Send(
            new RegisterTenantApplicationCommand(
                TenantId: tenantId,
                Platform: TenantApplicationPlatform.Android,
                PackageId: PackageId));
        android.IsSuccess.Should().BeTrue(android.ErrorMessage);

        var ios = await mediator.Send(
            new RegisterTenantApplicationCommand(
                TenantId: tenantId,
                Platform: TenantApplicationPlatform.Ios,
                PackageId: PackageId));
        ios.IsSuccess.Should().BeTrue(ios.ErrorMessage);

        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var apps = await db.TenantApplications.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.Platform)
            .ToListAsync();
        apps.Should().HaveCount(2);
        apps.Select(a => a.Platform).Should().BeEquivalentTo(new[]
        {
            TenantApplicationPlatform.Android,
            TenantApplicationPlatform.Ios,
        });
        apps.Should().OnlyContain(a => a.PackageId == PackageId);
    }

    [Fact]
    public async Task Register_for_unknown_tenant_returns_NotFound()
    {
        var unknownTenantId = new TenantId(Guid.NewGuid());

        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = await mediator.Send(
            new RegisterTenantApplicationCommand(
                TenantId: unknownTenantId,
                Platform: TenantApplicationPlatform.Android,
                PackageId: "com.example.bank"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);
        result.ErrorMessage.Should().Contain(unknownTenantId.Value.ToString("D"));
    }
}
