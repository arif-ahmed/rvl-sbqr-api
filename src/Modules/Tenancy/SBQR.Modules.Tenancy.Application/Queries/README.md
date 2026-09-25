# Queries

MediatR queries, one folder per query, each with `{Query}.cs`, `{Query}Handler.cs`.

Expected queries (Story 6):

- **`GetTenant/`** — admin read of a tenant by id.
- **`GetApiCredential/`** — admin read of a credential's metadata + hash prefix only;
  the response DTO (in `../Contracts/`) never includes `client_secret` or the full hash.

Admin endpoints in `SBQR.Modules.Tenancy.Api/Controllers/` are platform-staff-scoped, not
tenant-scoped — these queries must not accept or filter by `ICurrentTenant.TenantId`.
