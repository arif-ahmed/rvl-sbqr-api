using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Audit.Infrastructure;
using SBQR.Modules.Audit.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Audit.Api;

/// <summary>
/// Composition root for the Audit module. Registers the concrete
/// <see cref="IAuditLogger"/> implementation backed by
/// <see cref="AuditDbContext"/> and the <c>audit_logs</c> table
/// (migration <c>20260904120000_create_audit_logs_table.sql</c>).
/// Epic-9 wires the export endpoint.
/// </summary>
public sealed class AuditModule : IModule
{
    public string Name => "audit";

    public Assembly ApplicationPartAssembly =>
        typeof(SBQR.Modules.Audit.Domain.Persistence.AuditLog).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<AuditDbContext>((sp, opts) =>
        {
            var connectionString = configuration.GetConnectionString("sbqr_app")
                ?? throw new InvalidOperationException(
                    "Missing connection string 'sbqr_app' for AuditDbContext. " +
                    "Configure ConnectionStrings:sbqr_app in appsettings.json or environment variables.");

            opts.UseNpgsql(connectionString);
        });

        // Scoped: shares the request lifetime; stamps created_by/created_at
        // itself from the AuditEntry (no interceptor needed).
        services.AddScoped<IAuditLogger, AuditLogger>();
    }
}
