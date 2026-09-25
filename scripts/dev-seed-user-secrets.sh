#!/usr/bin/env bash
# dev-seed-user-secrets.sh — one-shot helper to populate `dotnet user-secrets`
# for the SBQR.Api project after a fresh clone.
#
# Usage:
#   ./scripts/dev-seed-user-secrets.sh
#
# After running this, `dotnet run --project src/Host/SBQR.Api` (with
# ASPNETCORE_ENVIRONMENT=Development, the default on a developer machine)
# resolves every connection string, signing key, and trust-store URL the
# host needs. Re-running is idempotent — `user-secrets set` overwrites
# existing values.
#
# Production / on-prem deployments MUST NOT use this script — production
# secrets are injected via environment variables by the host orchestrator
# (systemd EnvironmentFile, K8s env:, Windows service environment, ...).
# See AGENTS.md §"Non-Negotiable Constraints" / C3 + C9 and the README
# "Secrets" section.
#
# What this script seeds:
#   - Two local Postgres connection strings (matches docker-compose defaults).
#   - A DEV-ONLY HS256 JWT signing key (32+ chars). The host refuses this
#     key in Production (Program.cs §10a) — do not reuse it in any
#     environment beyond local development.
#   - The trust-store endpoint (TrustStore:BaseUrl) the host syncs from.
#   - Dev S3 vault selection + the shared dev bucket coordinates (region /
#     bucket / folder — non-secret). The base appsettings.json defaults
#     Crypto:VaultProvider to Local; dev overrides it back to S3 here.
#
# Personal AWS credentials (Storage:AccessKeyId / Storage:SecretAccessKey)
# are NOT seeded — set them manually after first run:
#   dotnet user-secrets set "Storage:AccessKeyId" "<aws key id>" --project src/Host/SBQR.Api/SBQR.Api.csproj
#   dotnet user-secrets set "Storage:SecretAccessKey" "<aws secret>" --project src/Host/SBQR.Api/SBQR.Api.csproj
#
# What this script deliberately does NOT seed:
#   - Auth:Bootstrap:ClientSecretHash — an Argon2id PHC string that must be
#     PERSONAL per developer (rotation hygiene). Generate once with:
#         dotnet run --project src/Host/SBQR.Api -- --generate-bootstrap-secret

set -euo pipefail

PROJECT="src/Host/SBQR.Api/SBQR.Api.csproj"

# Connection strings mirror docker-compose.yml — these are the canonical dev
# infra defaults (localhost:5432, postgres/postgres). They are NOT real
# secrets; they are documented disposable values that match the compose stack.
SBQR_APP_CS='Host=localhost;Port=5432;Database=sbqr_app;Username=postgres;Password=postgres;Include Error Detail=true'
SBQR_KEY_VAULT_CS='Host=localhost;Port=5432;Database=sbqr_key_vault;Username=postgres;Password=postgres;Include Error Detail=true'

# DEV-ONLY HS256 signing key (32+ chars). Mirrors docker-compose.yml line
# 180 — known disposable dev value across the project. Do not use in
# Production; the host refuses it at startup in Production (Program.cs §10a).
JWT_SIGNING_KEY='dev-only-signing-key-change-me-0123456789abcdef'

# Trust-store endpoint the host syncs from. Point at whatever trust store you
# test against — e.g. the ephemeral Node mock from tools/postman-export
# (port 5002). A dead endpoint only logs sync errors; the host still boots.
TRUST_STORE_BASE_URL='http://localhost:5002'

# Dev S3 vault + shared dev-bucket coordinates (NON-secret — shared team
# values, match docs/dev-s3-guide.md). Personal AWS credentials are never
# seeded here; set them manually (see header).
CRYPTO_VAULT='S3'
STORAGE_REGION='ap-southeast-1'
STORAGE_BUCKET='sbqr-dev-key-vault'
STORAGE_FOLDER='keycustody'

# Color output — matches generate-postman-export.sh.
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
NC='\033[0m'

if ! command -v dotnet >/dev/null 2>&1; then
  echo -e "${RED}dotnet CLI not found in PATH${NC}" >&2
  exit 1
fi

if [[ ! -f "${PROJECT}" ]]; then
  echo -e "${RED}Project file not found: ${PROJECT}${NC}" >&2
  echo "Run this from the repo root." >&2
  exit 1
fi

echo -e "${CYAN}SBQR.Api dev secrets seeder${NC}"
echo -e "${GRAY}project: ${PROJECT}${NC}"
echo -e "${GRAY}storage: $(dotnet user-secrets list --project "${PROJECT}" >/dev/null 2>&1 && echo 'user-secrets store exists') || echo 'user-secrets store will be initialized'${NC}"
echo

declare -a KEYS=(
  "ConnectionStrings:sbqr_app|${SBQR_APP_CS}"
  "ConnectionStrings:sbqr_key_vault|${SBQR_KEY_VAULT_CS}"
  "Jwt:SigningKey|${JWT_SIGNING_KEY}"
  "TrustStore:BaseUrl|${TRUST_STORE_BASE_URL}"
  # Dev-only cadence: sync the trust directory once at boot and every
  # minute after. Staging/prod never set these (silent-on-startup is the
  # default).
  "TrustStore:SyncOnStartup|true"
  "TrustStore:SyncIntervalMinutes|1"
  # Dev vault/backend selection + shared bucket coordinates.
  "Crypto:VaultProvider|${CRYPTO_VAULT}"
  "Storage:Region|${STORAGE_REGION}"
  "Storage:BucketName|${STORAGE_BUCKET}"
  "Storage:VaultFolder|${STORAGE_FOLDER}"
)

for entry in "${KEYS[@]}"; do
  key="${entry%%|*}"
  value="${entry#*|}"
  echo -e "${YELLOW}-> ${key}${NC}"
  dotnet user-secrets set "${key}" "${value}" --project "${PROJECT}" >/dev/null
done

echo
echo -e "${GREEN}Seeded 10 dev secrets into the per-user store.${NC}"
if ! dotnet user-secrets list --project "${PROJECT}" 2>/dev/null | grep -q 'Storage:AccessKeyId'; then
  echo -e "${YELLOW}Personal AWS credentials not found in user-secrets — set them manually (never committed):${NC}"
  echo -e "  ${CYAN}dotnet user-secrets set \"Storage:AccessKeyId\" \"<aws key id>\" --project ${PROJECT}${NC}"
  echo -e "  ${CYAN}dotnet user-secrets set \"Storage:SecretAccessKey\" \"<aws secret>\" --project ${PROJECT}${NC}"
fi
echo
echo -e "${YELLOW}One more key must be generated individually (one-time, personal):${NC}"
echo -e "  • ${CYAN}Auth:Bootstrap:ClientSecretHash${NC} — Argon2id PHC."
echo -e "      dotnet run --project src/Host/SBQR.Api -- --generate-bootstrap-secret"
echo -e "      dotnet user-secrets set \"Auth:Bootstrap:ClientSecretHash\" \"<phc>\" --project ${PROJECT}"
echo
echo -e "${GRAY}Verify with: dotnet user-secrets list --project ${PROJECT}${NC}"
