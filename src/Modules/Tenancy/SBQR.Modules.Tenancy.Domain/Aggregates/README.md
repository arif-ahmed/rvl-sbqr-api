# Aggregates

Aggregate roots for this bounded context. This project has no EF Core, no ASP.NET Core,
no `DbContext` awareness — pure domain behavior, per `tactical-design.md` §3.

- **`Tenant : AggregateRoot<TenantId>`** — Story 3. Exposes `institution_code`
  (the natural unique key, mirrors `institution_registries`), `institution_name`,
  `status`, `is_active`. State transitions (`Activate`, `Suspend`, `Deactivate`)
  are explicit methods that validate against a documented state machine and
  raise `IDomainEvent` subtypes from `../Events/`.
- **`ApiCredential : AggregateRoot<ApiCredentialId>`** — Story 4. Exposes `client_id`,
  `tenant_id`, `status`, `is_active`; no plaintext-secret property — only
  `client_secret_hash`. `Issue(...)` is a static factory that hashes on creation;
  `VerifyPlaintext(...)` and `Revoke()` are the only ways to interact with the secret.

Both extend `SBQR.SharedKernel.Domain.AggregateRoot<TId>` for identity/equality semantics
(Epic 1 Story 2).