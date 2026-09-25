# Interfaces

Repository ports the Domain layer depends on but does not implement — the implementation
lives in `SBQR.Modules.Tenancy.Infrastructure/Persistence/` (Story 2), per the Dependency
Inversion rule Clean Architecture requires: Domain defines the contract, Infrastructure
satisfies it (and, correspondingly, references Domain — never the other way around).

- **`ITenantRepository`** — Story 2/3. Load/save for `Tenant` by `TenantId`; lookup by
  `institution_code` (the natural unique key, mirrors `institution_registries`).
- **`IApiCredentialRepository`** — Story 2/4. Load/save for `ApiCredential` by
  `ApiCredentialId`; lookup by `client_id` (unique).

These extend or compose `SBQR.SharedKernel.Persistence.IRepository<T>` where it fits;
introduce a narrower interface only where the generic port doesn't cover a real query need
(e.g. lookup-by-unique-code).