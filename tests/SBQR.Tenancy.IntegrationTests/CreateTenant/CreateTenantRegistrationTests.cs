using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;
using SBQR.Tenancy.IntegrationTests.Infrastructure;
using Xunit;

namespace SBQR.Tenancy.IntegrationTests.CreateTenant;

/// <summary>
/// End-to-end happy-path coverage for the minimal
/// <see cref="CreateTenantCommand"/> flow. The handler inserts exactly one
/// <c>tenants</c> row and returns the new id — client-credential issuance and
/// cryptographic signing-key generation are out of scope (dedicated endpoints).
///
/// <para>
/// Runs against a real PostgreSQL 16-alpine container (not the EF Core InMemory
/// provider) so the UNIQUE / CHECK / FK constraints we want to prove are firing
/// are actually exercised.
/// </para>
///
/// <para>
/// Audit assertion contract: this handler does NOT call
/// <see cref="IAuditLogger"/> directly. The audit trail rides on the row's own
/// <c>created_by</c> / <c>created_at</c> columns, stamped by
/// <c>TenancyAuditColumnInterceptor</c> at <c>SavingChanges</c> time. A
/// separate <c>audit_logs</c> row would duplicate the row's own audit columns
/// and is intentionally omitted. The
/// <see cref="CapturingAuditLogger.Entries"/> assertion below pins zero
/// entries — if a future maintainer reintroduces the duplicate, this test
/// fires.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CreateTenantRegistrationTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _pg;
    private ServiceProvider _sp = default!;
    private CapturingAuditLogger _audit = default!;

    public CreateTenantRegistrationTests(PostgreSqlFixture pg)
    {
        _pg = pg ?? throw new ArgumentNullException(nameof(pg));
    }

    public async Task InitializeAsync()
    {
        // Clean slate — every user table is DELETEd (FK-safe), the schema
        // (CHECKs, UNIQUE indexes, FK constraints) was applied by the external
        // migration tool before PostgreSqlFixture.InitializeAsync ran.
        await _pg.ResetAsync();

        var (sp, audit) = TenancyHostBuilder.BuildForPostgres(_pg);
        _sp = sp;
        _audit = audit;
    }

    public async Task DisposeAsync()
    {
        if (_sp is not null)
        {
            await _sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task Registers_tenant_end_to_end()
    {
        // Arrange: hand-built command. institution_code is required at
        // registration (mirrors institution_registries.institution_code).
        const string InstitutionName = "Acme Bank Ltd";
        const string InstitutionCode = "010101";

        var command = new CreateTenantCommand(
            InstitutionName: InstitutionName,
            InstitutionCode: InstitutionCode);

        // Act + Assert: dispatch through the real MediatR pipeline inside an
        // async scope that mirrors the HTTP request scope SBQR.Api creates.
        await using (var scope = _sp.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(command);

            // ---- Result surface -----------------------------------------
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.Value.Should().NotBeNull();
            result.Value.TenantId.Value.Should().NotBe(Guid.Empty);

            // ---- DB surface (read back via EF Core, AsNoTracking) ------
            var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();

            var tenants = await db.Tenants.AsNoTracking().ToListAsync();
            tenants.Should().HaveCount(1, "CreateTenant must persist exactly one Tenant row");
            var tenant = tenants[0];
            tenant.InstitutionName.Should().Be(InstitutionName);
            tenant.InstitutionCode.Should().Be(InstitutionCode);
            tenant.Status.Should().Be(TenantStatus.Pending,
                "Tenant.Register creates in Pending; lifecycle transitions land in Story 5");
            tenant.IsActive.Should().BeTrue();
            tenant.CreatedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
            tenant.CreatedBy.Should().NotBeNullOrEmpty(
                "the audit interceptor stamps created_by via IActorProvider.CurrentActor()");

            // crypto_keys is now KeyCustody's table end-to-end (schema, reads,
            // and writes) — TenancyDbContext no longer maps it, so "no key
            // minted by registration" is asserted in KeyCustody's own test
            // suite instead of here.
        }

        // ---- Audit surface --------------------------------------------------
        // The Tenancy-side handler writes zero IAuditLogger entries. The audit
        // trail rides on the row's own created_by / created_at columns. If a
        // future maintainer reintroduces the duplicate IAuditLogger.LogAsync
        // call, this assertion fails.
        _audit.Entries.Should().BeEmpty(
            "CreateTenant is a single-table insert; audit trail rides on the row's created_by / created_at columns");
    }
}