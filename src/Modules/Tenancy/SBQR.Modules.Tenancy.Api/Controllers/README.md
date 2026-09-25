# Controllers

HTTP surface for this module (Story 6, Story 7). Tagged with the `Tenancy` module name in
the Epic 1 OpenAPI document.

- **`TenantsController`** — `/v1/admin/tenants/**`: `POST /v1/admin/tenants` (create — the one
  admin endpoint that runs without a resolved `ICurrentTenant`, since a brand-new tenant
  has no configuration yet); suspend/reactivate/terminate/activate actions; and the
  `POST /v1/admin/tenants/{id}/tenant-configuration` endpoint that mints the tenant's
  initial FI client configuration through the IdentityAccess Contracts seam
  (`TenantsController.ProvisionConfigurationAsync`, returns `clientSecret` exactly once).
- **`OAuth2TokenController`** — `POST /v1/oauth/token` (Story 7): client-credentials grant,
  returns a signed short-lived JWT with `tenant_id`, `client_id`, `iss`, `aud`, `iat`,
  `exp`. Failed auth returns a generic `401` (no credential-enumeration signal).

Rules enforced by `SBQR.ArchitectureTests` (Story 8):

- Every admin action under `/v1/admin/tenants/**` must carry an explicit
  `AllowAnonymous = false` / policy marker — the auth seam is *declared* here even though
  no platform-staff identity provider exists until Epic 6 lands.
- Admin endpoints never read `ICurrentTenant` to scope themselves — they operate across
  tenants by design (platform-staff-scoped, not tenant-scoped).
- All writes go through `IAuditLogger` (Epic 1 Story 10) with `actor`, action verb, target
  id, and outcome.
- Validators run through the Shared Kernel `ValidationBehavior<,>` — no inline `if`
  validation in controllers.
