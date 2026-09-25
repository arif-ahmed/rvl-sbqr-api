# BQR Secure Manager (SBQR)

The platform that issues and verifies BanglaQR P2P codes for Financial
Institutions. This glossary fixes the terms that are easy to conflate; it is a
vocabulary, not a spec (the spec is `docs/bb-banglaqr-p2p-specification.md`).

## Language

**FI (Financial Institution)**:
A bank or MFS that integrates with SBQR. The platform's tenant and customer.
_Avoid_: client, partner, org

**Tenant**:
An FI's identity *inside the platform*, keyed by `client_id`. All of an FI's
traffic funnels through one tenant, so limits and audit are scoped per-tenant.
_Avoid_: account, client (when you mean the FI's platform identity)

**Platform token**:
The short-lived (10 min) `client_credentials` JWT the FI backend obtains from
`POST /v1/oauth/token`. Its subject is the FI (`client:{client_id}`), never an
end user. Lives only in FI-backend memory — never on a device or in a browser.
_Avoid_: access token, bearer token (too generic), OAuth token

**User JWT**:
The per-end-user token issued by the *FI's own* OIDC IdP. The BFF verifies it
to authenticate the app user. Distinct from the platform token: one per user,
issued and owned by the FI, not the platform.
_Avoid_: platform token, session token

**BFF (Backend-for-Frontend)**:
The FI-side server the app talks to; it holds the FI's credentials and forwards
QR calls to the platform. In SBQR it is a **1:1 reverse proxy** (mirrors
`v1.public`, passes error bodies through unchanged) — call it a reverse proxy /
gateway when precision matters, because it deliberately does *not* reshape
responses per client the way a textbook BFF would.
_Avoid_: using "BFF" to justify per-client response logic the design excludes

**user_sub**:
The FI's *internal, opaque* subject id for an end user (e.g. `u-456`). Sent to
the platform as `X-User-Sub`. Must never be PII (no MSISDN, NID, or PAN).
_Avoid_: user id, customer id, msisdn

**user_sub_hash**:
`HMAC-SHA256(per-tenant salt, user_sub)` — what the *platform* logs. One-way:
it lets the platform group and count a user's activity without identifying them;
the FI resolves it back to a person on request (trace by join, not by decode).
_Avoid_: user hash, anonymized id

**Correlation ID**:
`X-Correlation-Id` — a per-request handle forwarded end-to-end. Joins a single
platform log line to the FI's BFF log for that request. Complements
`user_sub_hash` (which groups across requests).
_Avoid_: request id, trace id (when you specifically mean the forwarded join key)
