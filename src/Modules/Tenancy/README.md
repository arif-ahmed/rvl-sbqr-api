# Tenancy

Clean Architecture layout for the Tenancy & Access bounded context (Epic 3), split into
one project per layer. Deliberately thin per `docs/design/epics.md` — this module owns
the `tenants` table (and the temporary `crypto_keys` table for the institute-register
flow) and exists to unblock the Core epics (5, 8), not to grow into an IAM system. The
`tenant_configurations` aggregate (formerly `api_credentials`) and OAuth2
client-credentials plumbing live in `SBQR.Modules.IdentityAccess`. See
`docs/delivery/epic-3-tenancy-stories.md` for the full story backlog.

**Note on layout**: `docs/design/tactical-design.md` §7 originally recorded "one project
per module, folders per layer" as the deliberate default for every module, to avoid
"24 projects of ceremony." Tenancy is carved out as an explicit exception to that default —
one project per Clean Architecture layer (Contracts, Domain, Application, Infrastructure,
Api), per the alias pattern below — while the other 7 modules (`QrCodec`,
`InstitutionTrust`, `KeyCustody`, `QrGeneration`, `Verification`, `Audit`,
`IdentityAccess`) keep the single-project-per-module layout. See `tactical-design.md` §3/§7
for the recorded rationale and status of this exception.

## Projects (dependencies point inward)

```
SBQR.Modules.Tenancy.Contracts/         published cross-module language — zero dependencies
  ITenantAdmissionDirectory.cs   Tenant admission-state lookup consumed by IdentityAccess
                                 to reject mid-flight token mints for Suspended/Terminated
                                 tenants (tactical-design.md §4/§5)
  TenantAdmissionState.cs        enum: Pending, Active, Suspended, Terminated
  → references: none (no MediatR, no EF Core, no ASP.NET Core)

SBQR.Modules.Tenancy.Domain/           no framework dependencies
  Aggregates/       Tenant, CryptoKey (Story 3)
  Events/           IDomainEvent subtypes raised by the aggregates (Story 3)
  Interfaces/       ITenantRepository, ICryptoKeyRepository, ILocalKeyVault,
                   IKeyPairGenerator, IKeyPairValidator (Story 2-4)
  → references: SBQR.SharedKernel only

SBQR.Modules.Tenancy.Application/       orchestration — no EF Core, no ASP.NET Core types
  Commands/         CreateTenant, ActivateTenant, SuspendTenant, ReactivateTenant,
                    TerminateTenant (Story 6, 7)
  Queries/          GetTenantById, ListTenants (Story 6)
  Contracts/        application-facing DTOs (e.g. TenantResponse) used by handlers and the
                    Api layer. This is NOT the cross-module seam — that lives in
                    SBQR.Modules.Tenancy.Contracts above (tactical-design.md §4/§5).
  → references: Domain, Contracts, SBQR.SharedKernel

SBQR.Modules.Tenancy.Infrastructure/     EF Core + cross-cutting adapters
  Persistence/       TenancyDbContext + entity configs + repository implementations,
                     plus TenantAdmissionDirectory implementing ITenantAdmissionDirectory (Story 2)
  Security/          CurrentTenantMiddleware; ICurrentTenant real implementation (Story 5)
  → references: Domain, Application, Contracts (for ITenantAdmissionDirectory), SBQR.SharedKernel

SBQR.Modules.Tenancy.Api/                 composition root + HTTP surface
  Controllers/        /v1/admin/tenants/** lifecycle (Story 6)
  TenancyModule.cs      IModule composition root (Story 1 — already implemented)
  → references: Application, Infrastructure, SBQR.SharedKernel
  (Domain flows in transitively via Application/Infrastructure;
   Contracts flows in transitively via Application.)
```

`SBQR.Api` (the host) references only `SBQR.Modules.Tenancy.Api.csproj` — the other four
layer projects flow in transitively through standard `ProjectReference` chaining.

## Boundary rules (enforced by `SBQR.ArchitectureTests`, Story 8)

- No project under `Tenancy/` references any project outside `SBQR.SharedKernel` and its
  own sibling layer projects — never a sibling module.
- Other modules may reference **`SBQR.Modules.Tenancy.Contracts`** for the published
  cross-module seam (today: `ITenantAdmissionDirectory` + `TenantAdmissionState`). They
  may NOT reference `Domain`, `Infrastructure`, `Application/Commands|Queries`, or
  `Application/Contracts/` (the latter holds application-facing DTOs, not the
  published seam — `tactical-design.md` §4/§5).
- `Contracts` is dependency-free by design: no MediatR, no EF Core, no ASP.NET Core. Its
  implementation lives in `Infrastructure/Persistence/Repositories/TenantAdmissionDirectory.cs`
  and is registered as the `ITenantAdmissionDirectory` service in `TenancyModule`.
- `Domain` has zero knowledge of persistence or HTTP — no `DbSet`, no `DbContext`, no
  `HttpContext`, ever; it is also the only layer with no package references beyond
  `SBQR.SharedKernel`.
- `tenants.institution_code` is a logical link, not a FK — do not add a navigation
  property to `institution_registries` (Epic 4's bounded context).

## Current status

Story 1 is complete: all five `.csproj` files exist (Contracts, Domain, Application,
Infrastructure, Api), `Api/TenancyModule.cs` (`IModule` implementation), host registration
in `SBQR.Api/Program.cs`, and `SBQR.Modules.Tenancy.Tests` are in place. The Contracts
project was added after the OAuth 2.1 client-credentials surface migrated to
`SBQR.Modules.IdentityAccess` (the token endpoint needs tenant admission state without
referencing `Tenancy.Domain` or `Tenancy.Infrastructure`). The lifecycle endpoints
(`/v1/admin/tenants/{id}/{activate,suspend,reactivate,terminate}`) live here; the
`api_credentials` aggregate, the `POST /v1/oauth/token` endpoint, and all of OAuth2
client-credentials now live in IdentityAccess. The implementation of each folder is owned
by the story noted in its header — Stories 2–7 progressively populate the layers.
