#!/usr/bin/env bash
# Generate the Dart/Flutter SDK for BQR Secure Manager's v1.public API.
#
# Mirrors tools/postman-export/generate-postman-export.sh: starts the mock
# TrustStore + API, fetches the live v1.public OpenAPI document, patches in
# the bearer-auth security scheme and the oauth/token requestBody (see
# patch-openapi-security.js for why), then runs openapi-generator-cli
# (via Docker — no local Java/Dart SDK required) with the dart-dio generator.
#
# Output: sdks/dart/bqr_public_client/ (committed to the repo — this is the
# artifact the mobile team consumes; see sdks/dart/README.md for usage).
#
# Usage:
#   ./tools/dart-sdk-export/generate-dart-sdk.sh              # start services + generate
#   ./tools/dart-sdk-export/generate-dart-sdk.sh --skip-startup  # API already running on :5001

set -euo pipefail

SKIP_STARTUP=0
if [ "${1:-}" = "--skip-startup" ]; then
    SKIP_STARTUP=1
fi

API_PORT=5001
TRUST_STORE_PORT=5002
API_URL="http://localhost:${API_PORT}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
OPENAPI_DIR="${REPO_ROOT}/.openapi"
RAW_SPEC="${OPENAPI_DIR}/v1.public.json"
PATCHED_SPEC="${OPENAPI_DIR}/v1.public.patched.json"
SDK_OUT_DIR="${REPO_ROOT}/sdks/dart/bqr_public_client"
PATCH_SCRIPT="${SCRIPT_DIR}/patch-openapi-security.js"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
NC='\033[0m'

echo -e "${CYAN}🎯 BQR Dart SDK Generator (v1.public)${NC}"

mkdir -p "$OPENAPI_DIR"

cleanup() {
    if [ "$SKIP_STARTUP" -eq 0 ]; then
        echo -e "\n${YELLOW}🧹 Stopping background processes...${NC}"
        kill %1 %2 2>/dev/null || true
        wait 2>/dev/null || true
    fi
}
trap cleanup EXIT

# ============================================================================
# 1. Start services (unless --skip-startup)
# ============================================================================
if [ "$SKIP_STARTUP" -eq 0 ]; then
    echo -e "\n${CYAN}📦 Starting services...${NC}"

    if netstat -tuln 2>/dev/null | grep -q ":${API_PORT} "; then
        echo -e "${YELLOW}  ⚠ API port ${API_PORT} already in use; assuming API is running${NC}"
        SKIP_STARTUP=1
    else
        echo -e "${GRAY}  Starting mock TrustStore on port ${TRUST_STORE_PORT}...${NC}"
        node -e '
        const http = require("http");
        const server = http.createServer((req, res) => {
          if (req.url === "/trust-store/institutions" && req.method === "GET") {
            res.writeHead(200, { "Content-Type": "application/json" });
            res.end(JSON.stringify({ institutions: [] }));
          } else {
            res.writeHead(404, {});
            res.end("Not Found");
          }
        });
        server.listen(5002, "127.0.0.1", () => {
          console.log("Mock TrustStore listening on port 5002");
        });
        ' &
        echo -e "${GREEN}    ✓ TrustStore started (PID: $!)${NC}"
        sleep 1

        echo -e "${GRAY}  Starting API on port ${API_PORT}...${NC}"
        (cd "$REPO_ROOT" && dotnet run --project src/Host/SBQR.Api) &
        echo -e "${GREEN}    ✓ API started (PID: $!)${NC}"

        echo -e "${GRAY}  Waiting for API to be ready...${NC}"
        max_retries=60
        retries=0
        while [ $retries -lt $max_retries ]; do
            if curl -s -o /dev/null -w "%{http_code}" "${API_URL}/health/live" | grep -q "200"; then
                echo -e "${GREEN}    ✓ API is ready${NC}"
                break
            fi
            retries=$((retries + 1))
            sleep 0.5
        done
        if [ $retries -ge $max_retries ]; then
            echo -e "${RED}    ✗ API failed to respond after ${max_retries}0s${NC}"
            exit 1
        fi
    fi
fi

# ============================================================================
# 2. Fetch the live v1.public OpenAPI document
# ============================================================================
echo -e "\n${CYAN}📥 Fetching OpenAPI document...${NC}"
curl -sf "${API_URL}/openapi/v1.public.json" -o "$RAW_SPEC"
echo -e "${GRAY}  Saved: $RAW_SPEC${NC}"

# ============================================================================
# 3. Patch in bearer-auth + oauth/token requestBody (generation-tooling only
#    — see patch-openapi-security.js header comment for why this is needed)
# ============================================================================
echo -e "\n${CYAN}🔧 Patching OpenAPI document for SDK generation...${NC}"
node "$PATCH_SCRIPT" "$RAW_SPEC" "$PATCHED_SPEC"

# ============================================================================
# 4. Generate the Dart SDK via openapi-generator-cli (Docker — no local
#    Java/Dart SDK required)
# ============================================================================
echo -e "\n${CYAN}🐳 Generating Dart SDK (dart-dio) via Docker...${NC}"
mkdir -p "$SDK_OUT_DIR"

MSYS_NO_PATHCONV=1 docker run --rm \
    -v "${REPO_ROOT}:/local" \
    openapitools/openapi-generator-cli generate \
    -i "/local/.openapi/v1.public.patched.json" \
    -g dart-dio \
    -o "/local/sdks/dart/bqr_public_client" \
    --additional-properties=pubName=bqr_public_client,pubVersion=1.0.0,pubDescription="Dart/Flutter client for the BQR Secure Manager v1.public API (BanglaQR P2P QR generation and validation).",nullableFields=true,useEnumExtension=true

echo -e "\n${GREEN}✅ SDK generated: $SDK_OUT_DIR${NC}"
echo -e "\n${CYAN}📋 Next steps:${NC}"
echo -e "  1. Review the diff under sdks/dart/bqr_public_client/ (models/APIs should track the v1.public surface)"
echo -e "  2. Bump pubVersion in this script if the API surface changed in a breaking way"
echo -e "  3. Commit sdks/dart/bqr_public_client/ — the mobile team consumes it directly (see sdks/dart/README.md)"
