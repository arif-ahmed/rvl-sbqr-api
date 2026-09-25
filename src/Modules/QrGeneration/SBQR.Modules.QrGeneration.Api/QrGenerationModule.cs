using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.QrGeneration.Application.Commands;
using SBQR.Modules.QrGeneration.Infrastructure.Persistence;
using SBQR.Modules.QrGeneration.Infrastructure.Persistence.Interceptors;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.QrGeneration.Api;

/// <summary>
/// Composition root for the QrGeneration module: the QR-generation pipeline
/// (codec build → KeyCustody sign → CRC-last finalize) plus the
/// <c>qr_generations</c> persistence (payload hash only).
/// </summary>
public sealed class QrGenerationModule : IModule
{
    public string Name => "qr-generation";

    public Assembly ApplicationPartAssembly =>
        typeof(SBQR.Modules.QrGeneration.Application.Commands.GenerateStaticQr.GenerateStaticQrCommand).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<QrGenerationDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("sbqr_app")
                ?? throw new InvalidOperationException(
                    "Missing connection string 'sbqr_app' for QrGenerationDbContext.");
            options.UseNpgsql(connectionString);
            options.AddInterceptors(sp.GetRequiredService<QrGenerationAuditColumnInterceptor>());
        });

        services.AddScoped<QrGenerationAuditColumnInterceptor>();

        services.AddScoped<
            SBQR.Modules.QrGeneration.Application.Commands.Common.QrIssuancePipeline>();
        services.AddScoped<
            SBQR.Modules.QrGeneration.Application.Commands.GenerateStaticQr.IssueStaticQrService>();
        services.AddScoped<
            SBQR.Modules.QrGeneration.Application.Commands.GenerateDynamicQr.IssueDynamicQrService>();
    }
}
