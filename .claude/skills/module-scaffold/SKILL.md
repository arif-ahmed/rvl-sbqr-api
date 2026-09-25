---
name: module-scaffold
description: Scaffolds a new SBQR bounded-context module with 5 layered projects (Domain, Application, Infrastructure, Api, Contracts) under src/Modules/, plugs it into the SBQR.Api host and SBQR.slnx, and reviews whether each layer should reference SBQR.SharedKernel. Use when asked to "scaffold a module", "add a new bounded context", "create module projects", "plug module into host", or "review SharedKernel references".
---

# SBQR Module Scaffolding (5-project layered module)

Repos: **rvl-secure-bqr-manager (SBQR.Service)** — modular monolith on .NET 10.

This skill scaffolds a new bounded-context module following the **Tenancy exception
layout** (one project per Clean Architecture layer, see `docs/design/tactical-design.md`
§3/§7), extended with a dedicated **Contracts** project as the module's published
language. Reference example throughout: `src/Modules/Tenancy/`.

## 0. Inputs to collect before scaffolding

Ask the user (or infer + confirm) before generating anything:

1. **Module name** — PascalCase, singular bounded-context noun (e.g. `Billing`,
   `Notifications`). Produces prefix `SBQR.Modules.{{Module}}.*`.
2. **Does it have an HTTP surface?** — if NO, skip the Api project (4 projects) and
   treat it like QrCodec (pure library, no `IModule`). Default is YES.
3. **Does it own persistence?** — if NO, skip the Infrastructure project. Default YES.
4. **Cross-module consumers** — which existing modules will call it? (Determines what
   goes into Contracts.)
5. **Confirm the layered split is justified.** Repo default is single-project-per-module
   (tactical-design.md §7 "24 projects of ceremony"). The 5-project split is the
   recorded exception for modules that need independent layering. Get explicit
   user confirmation the new module warrants it.

Substitution convention below: `{{Module}}` = PascalCase name. Placeholders appear in
file paths, namespaces, and connection strings.

## 1. Canonical structure (dependencies point inward)

```
src/Modules/{{Module}}/
  README.md
  SBQR.Modules.{{Module}}.Domain/
    SBQR.Modules.{{Module}}.Domain.csproj
    Aggregates/          aggregate roots + entities + value objects
    Events/              IDomainEvent subtypes raised by the aggregates
    Interfaces/          IXxxRepository port definitions
  SBQR.Modules.{{Module}}.Application/
    SBQR.Modules.{{Module}}.Application.csproj
    Abstractions/        module-internal service interfaces
    Commands/{{UseCase}}/  command + handler + validator per use case
    Queries/             query + handler per read
    Mapping/             {{Module}}MappingProfile (AutoMapper)
    {{Module}}ApplicationMarker.cs   (marker for host's MediatR scan)
  SBQR.Modules.{{Module}}.Infrastructure/
    SBQR.Modules.{{Module}}.Infrastructure.csproj
    Persistence/         {{Module}}DbContext + Configurations/ + Repositories/ + Interceptors/ + UnitOfWork/
    Security/            module middleware / principal seams if any
  SBQR.Modules.{{Module}}.Api/
    SBQR.Modules.{{Module}}.Api.csproj
    Controllers/
    {{Module}}Module.cs  IModule composition root
  SBQR.Modules.{{Module}}.Contracts/
    SBQR.Modules.{{Module}}.Contracts.csproj
    (public integration events, cross-module DTOs/requests — the ONLY
     surface other modules may reference)
```

Hard reference rules (enforced by `tests/SBQR.ArchitectureTests/ModuleBoundaryTests.cs`):

| Project | May reference | Never references |
|---|---|---|
| Contracts | SharedKernel (after §5 review) | Domain, Application, Infrastructure, Api, any sibling module |
| Domain | SharedKernel | packages, persistence, HTTP, sibling modules |
| Application | Domain, Contracts, SharedKernel | EF Core, ASP.NET Core types, sibling modules |
| Infrastructure | Domain, Application, SharedKernel | ASP.NET Core types, sibling modules |
| Api | Application, Infrastructure, Contracts, SharedKernel | sibling modules |
| Host `SBQR.Api` | only the module's **Api** project | the module's other 4 projects directly |

## 2. Project files

Inherit everything from `Directory.Build.props` (net10.0, Nullable, ImplicitUsings,
TreatWarningsAsErrors, CPM). **Never add `Version` attributes to PackageReference** —
versions live in `Directory.Packages.props`. If a package is not yet pinned there, add
it (check the existing pinned versions first).

### 2.1 Domain

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    SBQR.Modules.{{Module}}.Domain — pure domain layer for the {{Module}}
    bounded context. No EF Core, no ASP.NET Core, no persistence or HTTP
    awareness. See ../README.md for the full per-layer breakdown.
  -->

  <PropertyGroup>
    <RootNamespace>SBQR.Modules.{{Module}}.Domain</RootNamespace>
    <AssemblyName>SBQR.Modules.{{Module}}.Domain</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\SharedKernel\SBQR.SharedKernel\SBQR.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

### 2.2 Application

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    SBQR.Modules.{{Module}}.Application — orchestration layer. MediatR
    commands/queries, FluentValidation validators, AutoMapper profile.
    No EF Core, no ASP.NET Core types. See ../README.md.
  -->

  <PropertyGroup>
    <RootNamespace>SBQR.Modules.{{Module}}.Application</RootNamespace>
    <AssemblyName>SBQR.Modules.{{Module}}.Application</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\SBQR.Modules.{{Module}}.Domain\SBQR.Modules.{{Module}}.Domain.csproj" />
    <ProjectReference Include="..\SBQR.Modules.{{Module}}.Contracts\SBQR.Modules.{{Module}}.Contracts.csproj" />
    <ProjectReference Include="..\..\..\SharedKernel\SBQR.SharedKernel\SBQR.SharedKernel.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MediatR" />
    <PackageReference Include="FluentValidation" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" />
    <PackageReference Include="AutoMapper" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Options" />
  </ItemGroup>

</Project>
```

### 2.3 Infrastructure (only if module owns persistence)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    SBQR.Modules.{{Module}}.Infrastructure — EF Core persistence for the
    {{Module}} bounded context. See ../README.md.
  -->

  <PropertyGroup>
    <RootNamespace>SBQR.Modules.{{Module}}.Infrastructure</RootNamespace>
    <AssemblyName>SBQR.Modules.{{Module}}.Infrastructure</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\SBQR.Modules.{{Module}}.Domain\SBQR.Modules.{{Module}}.Domain.csproj" />
    <ProjectReference Include="..\SBQR.Modules.{{Module}}.Application\SBQR.Modules.{{Module}}.Application.csproj" />
    <ProjectReference Include="..\..\..\SharedKernel\SBQR.SharedKernel\SBQR.SharedKernel.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>

</Project>
```

Only add crypto/identity packages when the module genuinely needs them, with a
comment explaining why (see Tenancy's Infrastructure csproj for the style).

### 2.4 Api

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    SBQR.Modules.{{Module}}.Api — composition root + HTTP surface. Hosts
    {{Module}}Module (IModule) and the module's controllers. Referenced
    directly by SBQR.Api; Domain, Application, Infrastructure, and Contracts
    flow in transitively via ProjectReference. See ../README.md.
  -->

  <PropertyGroup>
    <RootNamespace>SBQR.Modules.{{Module}}.Api</RootNamespace>
    <AssemblyName>SBQR.Modules.{{Module}}.Api</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SBQR.Modules.{{Module}}.Application\SBQR.Modules.{{Module}}.Application.csproj" />
    <ProjectReference Include="..\SBQR.Modules.{{Module}}.Infrastructure\SBQR.Modules.{{Module}}.Infrastructure.csproj" />
    <ProjectReference Include="..\SBQR.Modules.{{Module}}.Contracts\SBQR.Modules.{{Module}}.Contracts.csproj" />
    <ProjectReference Include="..\..\..\SharedKernel\SBQR.SharedKernel\SBQR.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

### 2.5 Contracts

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    SBQR.Modules.{{Module}}.Contracts — the published language of the
    {{Module}} bounded context. This is the ONLY project other modules
    may reference (tactical-design.md §4/§5). Keep it dependency-free:
    no MediatR, no EF Core, no ASP.NET Core. SharedKernel reference is
    conditional — see the skill's SharedKernel review section.
  -->

  <PropertyGroup>
    <RootNamespace>SBQR.Modules.{{Module}}.Contracts</RootNamespace>
    <AssemblyName>SBQR.Modules.{{Module}}.Contracts</AssemblyName>
  </PropertyGroup>

  <!-- Include the SharedKernel ProjectReference ONLY if the §5 review says so. -->

</Project>
```

Naming note: use **Contracts** (plural), matching the existing `Application/Contracts/`
folder convention. The user may say "Contract" — normalize to `Contracts`.

## 3. Composition root — `{{Module}}Module.cs`

Place in the Api project root. Model it on `TenancyModule.cs`:

```csharp
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.{{Module}}.Api;

/// <summary>
/// Composition root for the {{Module}} module. The host
/// (<c>SBQR.Api/Program.cs</c>) discovers this via the moduleTypes array
/// and calls <see cref="RegisterServices"/> at startup.
/// MediatR, FluentValidation, AutoMapper, AddControllers ApplicationParts,
/// and the global ValidationBehavior are registered by the host — this
/// module does NOT duplicate those registrations.
/// </summary>
public sealed class {{Module}}Module : IModule
{
    public string Name => "{{module-kebab}}";

    /// <summary>
    /// The Application assembly — the host scans it for MediatR handlers,
    /// validators, and mapping profiles. Points at the Application project
    /// (NOT this Api assembly).
    /// </summary>
    public Assembly ApplicationPartAssembly { get; } =
        typeof(SBQR.Modules.{{Module}}.Application.{{Module}}ApplicationMarker).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 1. DbContext ({{Module}} schema). Schema is owned by the canonical
        //    SQL scripts under db/migrations/*.sql and applied by the
        //    external migration tool — this context NEVER calls
        //    Database.Migrate() (PERSISTENCE_DECISIONS.md §3). Connection
        //    string name: "sbqr_app".
        // 2. Repositories (IXxxRepository -> implementations).
        // 3. Module-specific services, interceptors, options.
    }
}
```

Create the marker in the Application project:

```csharp
namespace SBQR.Modules.{{Module}}.Application;

/// <summary>Assembly marker used by the host's MediatR/FluentValidation/
/// AutoMapper scans via IModule.ApplicationPartAssembly.</summary>
internal sealed class {{Module}}ApplicationMarker;
```

## 4. Plug into the host

Four mandatory edits — the module does not exist to the host without all of them:

1. **`src/Host/SBQR.Api/SBQR.Api.csproj`** — add ONLY the Api project reference:

   ```xml
   <ProjectReference Include="..\..\Modules\{{Module}}\SBQR.Modules.{{Module}}.Api\SBQR.Modules.{{Module}}.Api.csproj" />
   ```

2. **`src/Host/SBQR.Api/Program.cs`** — add the module type to the `moduleTypes`
   array (~line 128, the block with `typeof(TenancyModule),` …). This drives
   MediatR/FluentValidation/AutoMapper scanning, `RegisterServices`, and
   ApplicationParts:

   ```csharp
   typeof({{Module}}Module),
   ```

3. **`SBQR.slnx`** — add the projects under a new module folder plus the folder
   declaration (keep lowest-dependency-first order):

   ```xml
   <Folder Name="/Modules/{{Module}}/">
     <Project Path="src/Modules/{{Module}}/SBQR.Modules.{{Module}}.Contracts/SBQR.Modules.{{Module}}.Contracts.csproj" />
     <Project Path="src/Modules/{{Module}}/SBQR.Modules.{{Module}}.Domain/SBQR.Modules.{{Module}}.Domain.csproj" />
     <Project Path="src/Modules/{{Module}}/SBQR.Modules.{{Module}}.Application/SBQR.Modules.{{Module}}.Application.csproj" />
     <Project Path="src/Modules/{{Module}}/SBQR.Modules.{{Module}}.Infrastructure/SBQR.Modules.{{Module}}.Infrastructure.csproj" />
     <Project Path="src/Modules/{{Module}}/SBQR.Modules.{{Module}}.Api/SBQR.Modules.{{Module}}.Api.csproj" />
   </Folder>
   ```

   And near the other `<Folder Name="/src/Modules/Tenancy/" />` declarations:
   `<Folder Name="/src/Modules/{{Module}}/" />`.

   Alternative: `dotnet sln SBQR.slnx add <paths>` — then verify/reorder manually.

4. **Architecture tests** — `tests/SBQR.ArchitectureTests/ModuleBoundaryTests.cs`:
   - Add the new module's assembly to the `moduleAssemblies` array in
     `NSec_should_only_be_referenced_from_KeyCustody`.
   - Add `"SBQR.Modules.{{Module}}.Domain"` and `"SBQR.Modules.{{Module}}.Infrastructure"`
     to the dependency deny-lists in `Modules_should_not_reference_other_modules_Domain_or_Infrastructure`.

Also scaffold (matching repo conventions):
- `src/Modules/{{Module}}/README.md` — copy `src/Modules/Tenancy/README.md` structure
  (projects table, boundary rules, reference list).
- `tests/SBQR.Modules.{{Module}}.Tests/` — mirror `tests/SBQR.Modules.Tenancy.Tests/`
  (csproj references Application/Api + Tests.Common); add to `/Tests/` in slnx.
- SQL migrations for new tables go in `db/migrations/*.sql` (applied by the
  external migration tool) — never `Database.Migrate()`.

## 5. SharedKernel reference review (run after scaffolding)

For each of the 5 projects, review whether the `SBQR.SharedKernel` ProjectReference
is actually needed, and whether anything new belongs in SharedKernel instead.

### 5.1 Does this layer need the reference?

| Layer | Needs SharedKernel when | Drop the reference when |
|---|---|---|
| Domain | Types derive `Entity<T>`, `ValueObject`, `AggregateRoot<T>`, implement `IDomainEvent` | Pure enum/static policy classes only (rare) |
| Application | Uses `Result`/`Result<T>`, `ValidationBehavior`, `ICurrentTenant`, `IAuditLogger`, `PolicyNames` | Handlers never touch those (rare) |
| Infrastructure | Implements `IRepository<T>`, `IUnitOfWork`, `IDbConnectionFactory`, uses `AuditColumnInterceptor` | No SharedKernel ports implemented (rare) |
| Api | Implements `IModule`, uses `PolicyNames`/`RateLimitPolicyNames` | No HTTP surface (module without Api project) |
| Contracts | Contract records expose `Result<T>`, shared IDs, or SharedKernel value types | **Common**: plain DTO records need nothing — keep Contracts dependency-free |

Verify empirically: scaffold, build, then grep the project for `SBQR.SharedKernel`
usages. Zero usages ⇒ remove the ProjectReference (unused references still flow
transitively and muddy the dependency story; `TreatWarningsAsErrors` will not catch
this for you).

### 5.2 Should a new type go into SharedKernel instead?

SharedKernel is the **last resort**, not a dumping ground. It must stay
zero-dependency (architecture test: `SharedKernel_should_have_zero_project_references`).
Add a type there ONLY if:

- Used by **3+ modules** or by the host AND multiple modules; AND
- It has no dependencies beyond BCL / MEL abstractions; AND
- It is genuinely stable (contract-like), not business logic.

If only the new module uses it ⇒ keep it in the module (Domain or Contracts).
Cross-module contract between exactly two modules ⇒ the consumer module references
the producer's **Contracts** project — NOT SharedKernel.

### 5.3 Contracts-specific checks

- Contracts must have zero package references and no FrameworkReference.
- Cross-module integration goes through Contracts records/events; other modules must
  never reference `{{Module}}.Domain`, `{{Module}}.Infrastructure`, or
  `{{Module}}.Application/Commands|Queries` directly.
- If a contract type duplicates something already in SharedKernel, reuse SharedKernel's.

## 6. Verification checklist (run in order)

```powershell
dotnet build SBQR.slnx                                    # TreatWarningsAsErrors must pass clean
dotnet test tests/SBQR.ArchitectureTests                  # boundary rules incl. new module entries
dotnet test tests/SBQR.Modules.{{Module}}.Tests           # scaffolded test project
dotnet build src/Host/SBQR.Api                            # host resolves the new module
```

Then confirm:
- [ ] Host boots and the new `IModule.RegisterServices` is hit (log/breakpoint).
- [ ] No `Version=` attributes leaked into any new csproj (CPM only).
- [ ] DbContext (if any) does not call `Database.Migrate()`.
- [ ] `.slnx` loads without warnings; new folder order is lowest-dependency first.
- [ ] README.md written; SharedKernel review (§5) recorded in the module README
      (one line per layer: referenced / dropped, and why).
