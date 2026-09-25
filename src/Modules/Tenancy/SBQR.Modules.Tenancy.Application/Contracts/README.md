# Contracts

The **only** surface other modules may reference from `SBQR.Modules.Tenancy.Application`
(`tactical-design.md` §4/§5 — cross-module calls happen only via a module's `Contracts/`
surface, enforced by `SBQR.ArchitectureTests`, Story 8). Everything else across the four
`SBQR.Modules.Tenancy.*` projects — `Domain`, `Infrastructure`, `Application/Commands|Queries`
— is invisible to sibling modules by convention and by architecture test.

Expected contents (Story 6, Story 7):

- Request/response DTOs for the admin CRUD and OAuth token endpoints (e.g.
  `TenantDto`, `ApiCredentialSummaryDto` — metadata + hash prefix, never the secret or full
  hash).
- Any MediatR command/query types that a *different* module is explicitly allowed to
  dispatch (expected to be rare or none for this epic — Epic 5/7/8 consume `ICurrentTenant`
  via the Shared Kernel seam, not by calling into Tenancy directly).
