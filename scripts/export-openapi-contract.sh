#!/usr/bin/env bash
# export-openapi-contract.sh — regenerate contracts/v1.public.json (PF-1).
#
# Usage:
#   ./scripts/export-openapi-contract.sh          # default port 5108
#   SBQR_CONTRACT_PORT=5200 ./scripts/...         # alternate port
#
# What this does:
#   1. Builds SBQR.Api (Debug).
#   2. Boots the compiled host in Development on a loopback port.
#   3. Captures GET /openapi/v1.public.json — the exact bytes the platform
#      serves, which IS the published contract downstream repos pin.
#   4. Stops the host and validates the capture parses as JSON before
#      replacing contracts/v1.public.json.
#
# Why capture-the-served-document instead of build-time generation:
#   The Microsoft.Extensions.ApiDescription.Server route boots the host in
#   document-generation mode but emits OpenAPI 3.1.1, ignoring this host's
#   deliberate OpenApiSpecVersion.OpenApi3_0 pin (Program.cs §8) — a
#   permanently-drifting artifact that no fetch-compare CI could ever trust.
#   Capturing the served endpoint makes the committed artifact byte-identical
#   to what any deployed instance serves.
#
# Prerequisites: a bootable Development configuration (user-secrets via
# scripts/dev-seed-user-secrets.sh, or a repo-root .env). No database is
# required — nothing on this path touches Postgres or the vault; the
# trust-store startup sync will log connection errors and continue.
#
# When to run it:
#   Whenever a v1.public API surface changes (new/changed controller,
#   route, DTO, or response shape). Commit the regenerated artifact with
#   the API change so downstream consumers (the FI gateway pins this file
#   for DTO codegen and drift CI) see the contract move in the same PR.

set -euo pipefail

PORT="${SBQR_CONTRACT_PORT:-5108}"
BASE_URL="http://127.0.0.1:${PORT}"
PROJECT="src/Host/SBQR.Api/SBQR.Api.csproj"
HOST_DLL="src/Host/SBQR.Api/bin/Debug/net10.0/SBQR.Api.dll"
CONTRACT="contracts/v1.public.json"
CAPTURE="${CONTRACT}.capture"

echo "[1/4] Building SBQR.Api (Debug)..."
dotnet build "$PROJECT" --configuration Debug --nologo --verbosity quiet

echo "[2/4] Booting host on ${BASE_URL} (Development, loopback only)..."
# ASPNETCORE_CONTENTROOT: running the DLL directly would otherwise treat the
# CWD as content root and fail to find appsettings.json (and the repo-root
# .env loader walks ../../.. from the content root, which must be the
# project directory, same as `dotnet run`).
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$BASE_URL" \
  ASPNETCORE_CONTENTROOT="$(pwd)/src/Host/SBQR.Api" \
  dotnet "$HOST_DLL" > sbqr-contract-export.log 2>&1 &
HOST_PID=$!
cleanup() {
  kill "$HOST_PID" 2>/dev/null || true
  wait "$HOST_PID" 2>/dev/null || true
}
trap cleanup EXIT

READY=0
for _ in $(seq 1 90); do
  if curl -sf "$BASE_URL/health/live" > /dev/null 2>&1; then READY=1; break; fi
  sleep 1
done
if [ "$READY" -ne 1 ]; then
  echo "Host did not become healthy within 90s — see sbqr-contract-export.log" >&2
  exit 1
fi

echo "[3/4] Capturing /openapi/v1.public.json ..."
HTTP_CODE="$(curl -s -o "$CAPTURE" -w '%{http_code}' "$BASE_URL/openapi/v1.public.json")"
if [ "$HTTP_CODE" != "200" ]; then
  echo "Document request returned HTTP ${HTTP_CODE}" >&2
  exit 1
fi

echo "[4/4] Validating and publishing ${CONTRACT} ..."
mkdir -p contracts
# The shared normalizer (also used by the ps1 variant) validates the capture
# and strips the environment-specific `servers` block so the published
# artifact is byte-identical regardless of which machine ran the export.
dotnet run scripts/normalize-openapi-artifact.cs "$CAPTURE" "$CONTRACT"
rm -f "$CAPTURE"
echo "Exported: ${CONTRACT} ($(wc -c < "$CONTRACT") bytes)"
