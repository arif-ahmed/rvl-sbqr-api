# Persistence

EF Core persistence for this module's two tables — Story 2.

- **`TenancyDbContext : DbContext`** — extends the Shared Kernel base
  (`tactical-design.md` §3); declares `DbSet<Tenant>` and `DbSet<ApiCredential>` (types
  from `SBQR.Modules.Tenancy.Domain/Aggregates/`). Audit columns (`created_by`,
  `created_at`, `modified_by`, `modified_at`) are populated by the Shared Kernel
  audit-column interceptor from Epic 1 — do not set them manually in handlers.
- **Entity configurations** (`IEntityTypeConfiguration<T>`) — `institution_code` unique;
  `client_id` unique; `ApiCredential.tenant_id` FK to `tenants.tenant_id` with **RESTRICT**
  cascade behavior (deleting a tenant with active credentials must fail loudly, never
  cascade).
- **`Repositories/`** — implementations of `SBQR.Modules.Tenancy.Domain/Interfaces/ITenantRepository`
  and `IApiCredentialRepository`, backed by `TenancyDbContext`.
- A single initial EF migration must produce SQL matching the canonical `schema.sql`
  definitions for `tenants` and `api_credentials` — no drift.
