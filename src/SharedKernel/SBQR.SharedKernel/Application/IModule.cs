using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SBQR.SharedKernel.Application;

/// <summary>
/// Composition contract for a bounded-context module.
///
/// Each module ships a concrete <c>IModule</c> implementation (e.g.
/// <c>KeyCustodyModule</c>). The host (<see cref="SBQR.Api"/>) enumerates
/// all <see cref="IModule"/> implementations at startup and calls
/// <see cref="RegisterServices"/> so each module can wire its own
/// MediatR handlers, FluentValidation validators, AutoMapper profile,
/// and DbContext.
///
/// <see cref="ApplicationPartAssembly"/> is added to the host's
/// <c>AddControllers().AddApplicationPart(...)</c> call so that the
/// module's controllers are served from the same Web API process.
///
/// See tactical-design.md §4.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Stable, lowercase, dotted identifier for the module
    /// (e.g. <c>"key-custody"</c>, <c>"qr-generation"</c>). Used in OpenAPI
    /// tags and in structured log scopes.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Register the module's services into the application's DI container.
    /// The module owns the choice of how to register its DbContext,
    /// MediatR handlers, validators, AutoMapper profile, and any
    /// module-specific services.
    /// </summary>
    /// <param name="services">The application's <see cref="IServiceCollection"/>.</param>
    /// <param name="configuration">The application's <see cref="IConfiguration"/>.</param>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// The assembly containing controllers and any other ASP.NET Core
    /// application parts owned by this module. The host uses this when
    /// calling <c>AddApplicationPart</c>.
    /// </summary>
    Assembly ApplicationPartAssembly { get; }
}
