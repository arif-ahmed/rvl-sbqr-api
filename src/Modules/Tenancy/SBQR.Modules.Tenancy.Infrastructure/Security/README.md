# Security

Story 5 — the seam that lets every other module resolve the acting tenant through the
Shared Kernel's `ICurrentTenant`, replacing Epic 1's no-op registration.

- **`CurrentTenantMiddleware`** — reads the bearer credential from the inbound request,
  calls `ApiCredential.VerifyPlaintext` (Story 4), and on success populates
  `HttpContext.Items["tenant_id"]`. On failure it short-circuits with `401 Unauthorized`
  and an audit-friendly reason code. `tenant_id` never enters the system from a request
  body, header, or query string — only from the validated credential.
- **`CurrentTenantAccessor : ICurrentTenant`** — reads the resolved tenant from the current
  `IHttpContextAccessor` scope. If no scope is active, throws `InvalidOperationException`
  (documented failure mode, not a silent default). It lives in `Infrastructure` (rather
  than `Application`) because it depends on `IHttpContextAccessor`, an infrastructure
  concern — keep it out of `Domain` and `Application/Commands|Queries` regardless.

Wiring: `TenancyModule.RegisterServices` (in `SBQR.Modules.Tenancy.Api`) replaces the
host's no-op `ICurrentTenant` registration with `CurrentTenantAccessor` exactly once this
module is registered.
