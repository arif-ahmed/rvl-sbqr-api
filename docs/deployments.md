# Deployments

What runs in each environment and the resources behind it.
Tiers, promotion, branching: [`environments.md`](environments.md).
Step-by-step server setup and updates: [`deploy-runbook.md`](deploy-runbook.md).

## Conventions (hosted tiers)

All AWS resources live in `ap-southeast-1`, prefixed `rvl-sbqr-`, suffixed
`-dev` / `-staging` / `-prod`. Identical on every tier:

* Servers: Ubuntu 24.04 LTS — systemd service `sbqr-api`, secrets in
  `/etc/sbqr/sbqr.env` (root-owned, `chmod 600`).
* Database: RDS PostgreSQL 16, 20 GiB gp3, initial DB `sbqr_app`, master
  user `sbqr_admin`, no public access.
* Vault: private S3 bucket (all public access blocked); a programmatic-only
  IAM user + policy scoped to that bucket.
* Firewalls: EC2 SG allows SSH (your IP) + HTTP 80 + HTTPS 443; the DB SG
  allows PostgreSQL from the EC2 SG only.
* Key pair and SGs follow the prefix/suffix pattern (`rvl-sbqr-key-dev`,
  `rvl-sbqr-ec2-sg-dev`, `rvl-sbqr-db-sg-dev`, …).

## local — developer machine

One command runs everything; no AWS resources except the shared vault bucket.

| Resource | What runs | Port |
|---|---|---|
| SBQR.Api | `sbqr.api` container — built from `docker/Dockerfile.api` | 8080 |
| SBQR database | `sbqr.postgres` — `postgres:16-alpine` + `sbqr.pgdata` volume; creates `sbqr_app`, `sbqr_key_vault` | 5432 |
| SBQR vault | shared S3 bucket — personal `Storage__VaultFolder`, credentials in `docker/.env` | — |
| DB admin | Adminer | 8081 |

* Secrets via `docker/.env` and user-secrets — test keys only, never real
  credentials. Schema via the `db/migrations/*.sql` loop (README §8).

## Development

* **SBQR.Api** — EC2 `rvl-sbqr-api-dev` · `t3.medium`
* **SBQR database** — RDS `rvl-sbqr-db-dev` · `db.t4g.small` · single-AZ · storage autoscaling off
* **SBQR vault** — S3 `rvl-sbqr-key-vault-dev`
* **Vault access** — IAM `rvl-sbqr-s3-dev` + policy `rvl-sbqr-s3-policy-dev`

* Deploys post-merge HEAD of `develop`; `ASPNETCORE_ENVIRONMENT=Development`.
* Fixed public IP via Elastic IP (`rvl-sbqr-eip-dev`); HTTP served at
  `api.dev.sbqr.<company-domain>` — see `devops-handoff-development.md`.
* Test keys and test trust store only. The current server still runs the
  `Production` env value from the retired playbook — flip at next redeploy.

## Staging

* **SBQR.Api** — EC2 `rvl-sbqr-api-staging` · `t3.medium`
* **SBQR database** — RDS `rvl-sbqr-db-staging` · `db.t4g.small` · single-AZ
* **SBQR vault** — S3 `rvl-sbqr-key-vault-staging`
* **Vault access** — IAM `rvl-sbqr-s3-staging` + policy `rvl-sbqr-s3-policy-staging`

* Deploys the `staging` branch only; `ASPNETCORE_ENVIRONMENT=Staging`.
* Production-like configuration and sanitized data, but staging keys.

## Production

* **SBQR.Api** — EC2 `rvl-sbqr-api-prod` · `t3.medium` minimum, size up per load
* **SBQR database** — RDS `rvl-sbqr-db-prod` · `db.t4g.small` minimum · **Multi-AZ** · deletion protection · backups · autoscaling on
* **SBQR vault** — S3 `rvl-sbqr-key-vault-prod` — real Ed25519 keys
* **Vault access** — IAM `rvl-sbqr-s3-prod` + policy `rvl-sbqr-s3-policy-prod`

* Deploys **tagged releases only** (`vX.Y.Z` on `main`).
* Real BB trust-store keys; Production startup guards enforced.
* HTTPS via certbot once a domain is attached.
