# Events

`IDomainEvent` subtypes raised by the aggregates in `../Aggregates/` (`SBQR.SharedKernel.Domain.IDomainEvent`, Epic 1 Story 2).

- **`TenantActivated`, `TenantSuspended`, `TenantDeactivated`** — Story 3, raised by
  `Tenant`'s lifecycle methods. Epic 9 (Audit) subscribes to these.
- **`ApiCredentialRevoked`** — Story 4, raised by `ApiCredential.Revoke()`.

Events are plain records describing what happened, not commands — they carry the
aggregate id and the relevant state, nothing else. No handler code lives here; subscribers
(e.g. Audit) live in their own modules and react via the Shared Kernel event-dispatch seam.
