# Commands

MediatR commands, one folder per command, each with `{Command}.cs`, `{Command}Handler.cs`,
`{Command}Validator.cs` — mirrors the `KeyCustody` module's `ActivateSigningKey/` pattern
from `tactical-design.md` §3.

Expected commands (Story 6, Story 7):

- **`CreateTenant/`** — admin-scoped; server-assigns `tenant_id` (no inbound credential
  exists yet for a brand-new tenant).
- **`IssueApiCredential/`** — admin-scoped; returns the plaintext `client_secret` exactly
  once in the response, never again.
- **`RevokeApiCredential/`** — admin-scoped; idempotent.
- **`ExchangeClientCredentialsForToken/`** — the `/oauth/token` handler (Story 7); calls
  `ApiCredential.VerifyPlaintext` and returns a signed JWT on success, generic 401 on
  failure (no credential-enumeration signal).

Validators run through the Shared Kernel `ValidationBehavior<,>` (Epic 1 Story 7) — no
inline `if` validation in handlers.
