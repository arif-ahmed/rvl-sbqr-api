using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Verification.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Verification.Api;

/// <summary>
/// Composition root for the Verification module: the fail-closed validate
/// pipeline (replay window → codec parse → InstitutionTrust key resolution
/// per spec Annex B → Ed25519 verify → historical key fallback → record
/// outcome) plus the <c>public.qr_validations</c> persistence —
/// one row per request, whose (tenant_id, request_id) unique index is the
/// C6 replay guard.
public sealed class VerificationModule : IModule
{
    public string Name => "verification";

    public Assembly ApplicationPartAssembly =>
        typeof(SBQR.Modules.Verification.Application.Commands.ValidateQrCommand).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<VerificationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("sbqr_app")
                ?? throw new InvalidOperationException(
                    "Missing connection string 'sbqr_app' for VerificationDbContext.");
            options.UseNpgsql(connectionString);
        });
    }
}
