namespace SBQR.Modules.IdentityAccess.Application;

/// <summary>
/// Assembly marker used by the host's MediatR / FluentValidation /
/// AutoMapper scans via <c>IModule.ApplicationPartAssembly</c>. The handlers,
/// validators, and mapping profile of this module live in the Application
/// project — never in the Api assembly (see the Tenancy post-mortem in
/// <c>SBQR.Api/Program.cs</c> §2 for what happens when the scan points the
/// wrong way).
/// </summary>
internal sealed class IdentityAccessApplicationMarker;
