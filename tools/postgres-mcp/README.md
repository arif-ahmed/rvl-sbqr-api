# SBQR.PostgresMcp — local PostgreSQL MCP server (dev only)

Stdio MCP server that lets coding agents inspect and query the **local**
SBQR databases (`sbqr_app` + `sbqr_key_vault`). Not part of the runtime,
not in `SBQR.slnx`, never deployed. Full guide: `docs/dev-mcp-postgres.md`.

## Build

The client registration uses `--no-build` so build output can never
corrupt the stdio protocol stream — always build explicitly:

```powershell
dotnet build tools/postgres-mcp -c Release
```

## Configure

Copy the keys from `env.example` into the repo-root `.env` (gitignored).
Every `ConnectionStrings__*` key becomes a named connection; real
environment variables override the file.

## Tools

| Tool | Purpose |
| ---- | ------- |
| `pg_list_connections` | Configured connections (names, hosts, write mode) |
| `pg_list_databases` | Databases on the cluster behind a connection |
| `pg_list_tables` | Tables/views with schema, estimated rows, size |
| `pg_describe_table` | Columns, constraints (PK/FK/CHECK), indexes |
| `pg_query` | Read-only SQL → markdown table (row-capped, cell-truncated) |
| `pg_execute` | Write SQL — disabled unless `POSTGRES_MCP_ALLOW_WRITES=true` |

## Safety notes (constraints)

- Local hosts only at startup (`localhost`/`127.0.0.1`/`::1`, plus
  `POSTGRES_MCP_ALLOWED_HOSTS`); anything else hard-fails.
- `POSTGRES_MCP_ALLOW_WRITES=true` with a `prod`/`stage`/`release`
  environment name hard-fails at startup (C20).
- Connection strings never appear in tool output or logs (C19); errors
  carry SqlState + message only (C13).
- Always filter by the tenant column where the table carries one (C5).
