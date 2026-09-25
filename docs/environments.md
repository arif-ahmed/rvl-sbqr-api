# Environments & Branching

SBQR moves through four environments. Code advances **only via pull request** —
every shared branch is locked against direct pushes. This document is the
standard for environments, promotion, hotfixes, and branch naming.

## At a glance

| # | Environment | Purpose | Branch | Code arrives by | Hosting | `ASPNETCORE_ENVIRONMENT` |
|---|---|---|---|---|---|---|
| 1 | **local** | day-to-day development | feature branches | — | developer machine (docker-compose) | `Development` |
| 2 | **Development** | first shared deployment; features verified end-to-end | `develop` | PR: feature → `develop` | AWS EC2 + RDS + S3 | `Development` |
| 3 | **Staging** | UAT sign-off before release | `staging` | PR: `develop` → `staging` | AWS EC2 + RDS + S3 | `Staging` |
| 4 | **Production** | live traffic | `main` | PR: `staging` → `main` | AWS EC2 + RDS + S3 | `Production` |

> **Tier names = `ASPNETCORE_ENVIRONMENT` values.** The three hosted tiers
> carry the ASP.NET Core standard names exactly; `local` also runs
> `Development` — same dev mode, different machine and injected config.

## Promotion flow

```
local → PR → develop ────→ Development    (feature verification)
              │
              PR → staging ─→ Staging     (UAT / sign-off)
                      │
                      PR → main ───→ Production  (release; tagged)
```

Rules:

* One direction only. Code never moves backwards; fixes land on `develop`
  and ride the full pipeline (except hotfixes — see below).
* Every promotion is a PR with **1 approval + CI green**
  (build + `dotnet test SBQR.slnx`).
* `develop`, `staging`, and `main` are locked: no direct pushes, ever.
* The `staging` branch is created from `develop` at the first promotion
  to Staging.

## The environments

### 1. Local — developer machine

* All active development, debugging, and unit/architecture tests run here.
* Stack via `docker compose -f docker/docker-compose.yml up`: API,
  PostgreSQL (`sbqr_app` + `sbqr_key_vault`), Adminer.
* Secrets via the repo-root `.env` and/or `dotnet user-secrets` — both are
  **Development-only** channels (see the README's § Secrets). Never real
  credentials: test keys, local Postgres defaults, mock trust store.
* No protection rules — free to experiment, reset, and break things.

### 2. Development — first shared deployment

* First real deployment of every feature; features are verified end-to-end
  here (QR generation → verify round-trips) against a shared Development
  database before they are considered "done".
* Branch `develop`, locked; receives feature/fix/chore/docs branches via PR.
* Data & secrets: shared Development database, **test keys and test trust
  store only** — never production keys. All config injected as environment
  variables by the host; no `.env` files at runtime.

### 3. Staging — UAT

* Release-candidate environment for pre-production validation and UAT
  sign-off; nothing reaches Production without passing through here
  (hotfix emergency bypass excepted).
* Branch `staging`, locked; receives **only** PRs from `develop` — no
  feature work lands here directly, only promotion of already-verified
  Development work.
* Data & secrets: production-like configuration and sanitized /
  production-like data. Behavior as close to Production as practical.

### 4. Production

* The live environment: real BanglaQR issue/verify traffic.
* Branch `main`, locked; receives **only** PRs from `staging`.
* Releases are tagged `vX.Y.Z` on `main` at merge time.
* Data & secrets: Production database, real Ed25519 private keys in the
  key vault, real BB trust-store public keys. All secrets via host
  environment variables only — zero secrets in source control.

## Hotfix process

Hotfixes are the single sanctioned exception to forward-only flow, and they
still pass through Staging — at reduced depth.

1. **Cut `hotfix/<slug>` from `main`** — never from `develop`. `main` is what
   runs in Production; branching from `develop` would drag unverified work
   into the fix.
2. Fix and test locally, then **PR `hotfix/* → staging`** with a *reduced*
   gate: 1 approval, CI green, and a short smoke check (QR round-trip +
   verify path) instead of full UAT.
3. Once Staging smoke passes, **PR the same commits `→ main`**, tag the
   patch release (`vX.Y.Z+1`), deploy.
4. **Back-merge immediately**: `main → staging` and `main → develop` (merge
   or cherry-pick) so the next normal promotion doesn't silently regress
   the fix.

**Emergency bypass.** GitHub branch protection on `main` may name one or two
maintainers as bypassers. When Production is actively broken they may merge
`hotfix/* → main` directly, skipping the Staging step; they owe a short
post-incident note, and the back-merge (step 4) still happens. The bypass
list is the only sanctioned path to `main` that skips Staging — branches are
never unprotected to ship a fix.

## Branch naming standard

Pattern: `<type>/<short-kebab-slug>`.

| Type | Purpose | Base | Merges into |
|---|---|---|---|
| `feature/…` | new functionality | `develop` | `develop` |
| `fix/…` | bug found before Production | `develop` | `develop` |
| `hotfix/…` | urgent Production fix | `main` | `staging`, then `main` |
| `chore/…` | deps, CI, tooling, build | `develop` | `develop` |
| `docs/…` | documentation only | `develop` | `develop` |

Ground rules:

* Long-lived branches are exactly **`main`, `staging`, `develop`** —
  lowercase, fixed spelling, all protected. No ad-hoc or personal
  long-lived branches.
* Optional issue prefix (`feature/12-dart-sdk-export`) — use it only on
  branches tied to a GitHub issue; a number that's sometimes there is worse
  than none.
* Kebab-case slugs, no personal names — git blame and the PR author
  already record who did the work.
* Delete the branch on merge (enable GitHub's "Automatically delete head
  branches"); the branch list should only ever show live work.

## Branch protection (GitHub)

Applied to `develop`, `staging`, and `main`:

* Require a pull request before merging; **1 approval** minimum.
* Require CI status (build + test suite) to pass before merge.
* Block direct pushes — including administrators (maintainers use the
  documented bypass on `main` only, for hotfix emergencies).
* Restrict force-pushes and branch deletion entirely.
