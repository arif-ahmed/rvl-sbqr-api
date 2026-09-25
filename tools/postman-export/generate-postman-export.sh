#!/usr/bin/env bash
# Generate Postman collection + environment files for BQR Secure Manager.
#
# This script is intended to be run by platform admins and the technical dev team
# against a local or staging instance they already control — so the internal-admin
# OpenAPI doc is fetched without HTTP Basic auth.
#
# Configuration:
#   - Built-in defaults apply when tools/postman-export/.postman.env is missing.
#   - If tools/postman-export/.postman.env exists, KEY=VALUE pairs are loaded on
#     top of the built-in defaults (POSTMAN_LOCAL_BASE_URL, POSTMAN_DEV_BASE_URL,
#     POSTMAN_STAGE_BASE_URL, POSTMAN_PROD_BASE_URL).
#   - See tools/postman-export/.postman.env.example for the tracked template.
#
# Usage:
#   ./tools/postman-export/generate-postman-export.sh         # all scopes (default)
#   ./tools/postman-export/generate-postman-export.sh public
#   ./tools/postman-export/generate-postman-export.sh internal
#   ./tools/postman-export/generate-postman-export.sh all

set -euo pipefail

SCOPE="${1:-all}"
DOCS_USERNAME="platform-team"
API_PORT=5001
TRUST_STORE_PORT=5002
API_URL="http://localhost:${API_PORT}"
TRUST_STORE_URL="http://localhost:${TRUST_STORE_PORT}"

# Resolve script-relative paths so it works regardless of CWD.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PYTHON_SCRIPT="${REPO_ROOT}/.claude/skills/postman-export/scripts/generate_postman.py"
OUTPUT_DIR="${REPO_ROOT}/.postman"
POSTMAN_ENV_FILE="${SCRIPT_DIR}/.postman.env"

# Validate scope
case "$SCOPE" in
    public|internal|all) ;;
    *)
        echo "Error: scope must be one of: public, internal, all (got: '$SCOPE')" >&2
        exit 2
        ;;
esac

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

# ============================================================================
# 0. Built-in defaults, then layer .postman.env on top (if present)
# ============================================================================
LOCAL_BASE_URL="http://localhost:5001"
DEV_BASE_URL="http://localhost:5001"
STAGE_BASE_URL="https://stage.bqr.internal"
PROD_BASE_URL="https://api.bqr.example.com"

if [ -f "$POSTMAN_ENV_FILE" ]; then
    set -a
    # shellcheck disable=SC1090
    . "$POSTMAN_ENV_FILE"
    set +a
    [ -n "${POSTMAN_LOCAL_BASE_URL:-}" ] && LOCAL_BASE_URL="$POSTMAN_LOCAL_BASE_URL"
    [ -n "${POSTMAN_DEV_BASE_URL:-}"   ] && DEV_BASE_URL="$POSTMAN_DEV_BASE_URL"
    [ -n "${POSTMAN_STAGE_BASE_URL:-}" ] && STAGE_BASE_URL="$POSTMAN_STAGE_BASE_URL"
    [ -n "${POSTMAN_PROD_BASE_URL:-}"  ] && PROD_BASE_URL="$POSTMAN_PROD_BASE_URL"
fi

# Allow env-var override on the command line too (handy in CI / make targets).
[ -n "${POSTMAN_LOCAL_BASE_URL:-}" ] && LOCAL_BASE_URL="$POSTMAN_LOCAL_BASE_URL"
[ -n "${POSTMAN_DEV_BASE_URL:-}"   ] && DEV_BASE_URL="$POSTMAN_DEV_BASE_URL"
[ -n "${POSTMAN_STAGE_BASE_URL:-}" ] && STAGE_BASE_URL="$POSTMAN_STAGE_BASE_URL"
[ -n "${POSTMAN_PROD_BASE_URL:-}"  ] && PROD_BASE_URL="$POSTMAN_PROD_BASE_URL"

echo -e "${CYAN}🔵 BQR Postman Export Generator${NC}"
echo -e "${GRAY}Scope: $SCOPE | Local URL: $LOCAL_BASE_URL | Dev URL: $DEV_BASE_URL${NC}"

# Cleanup function
cleanup() {
    echo -e "\n${YELLOW}🧹 Stopping background processes...${NC}"
    kill %1 %2 2>/dev/null || true
    wait 2>/dev/null || true
}

trap cleanup EXIT

# ============================================================================
# 1. Start services
# ============================================================================
echo -e "\n${CYAN}📦 Starting services...${NC}"

# Check if ports are in use
if netstat -tuln 2>/dev/null | grep -q ":${API_PORT} " || lsof -i :${API_PORT} 2>/dev/null; then
    echo -e "${YELLOW}  ⚠ API port ${API_PORT} already in use; assuming API is running${NC}"
else
    echo -e "${GRAY}  Starting mock TrustStore on port ${TRUST_STORE_PORT}...${NC}"

    # Shared mock (also runnable standalone for the Aspire AppHost
    # loop — see docs/aspire-local-dev-guide.md).
    node tools/postman-export/mock-trust-store.js &
    TRUST_STORE_PID=$!
    echo -e "${GREEN}    ✓ TrustStore started (PID: $TRUST_STORE_PID)${NC}"
    sleep 1

    echo -e "${GRAY}  Starting API on port ${API_PORT}...${NC}"
    dotnet run --project src/Host/SBQR.Api &
    API_PID=$!
    echo -e "${GREEN}    ✓ API started (PID: $API_PID)${NC}"

    # Wait for API to be ready
    echo -e "${GRAY}  Waiting for API to be ready...${NC}"
    max_retries=60
    retries=0
    while [ $retries -lt $max_retries ]; do
        if curl -s -o /dev/null -w "%{http_code}" "${API_URL}/health" | grep -q "200"; then
            echo -e "${GREEN}    ✓ API is ready${NC}"
            break
        fi
        retries=$((retries + 1))
        if [ $((retries % 10)) -eq 0 ]; then
            echo -e "${GRAY}    ⏳ Still waiting... ($retries/$max_retries)${NC}"
        fi
        sleep 0.5
    done

    if [ $retries -ge $max_retries ]; then
        echo -e "${YELLOW}    ⚠ API failed to respond after ${max_retries}s${NC}"
    fi
fi

# ============================================================================
# 2. Resolve scopes to export
# ============================================================================
echo -e "\n${CYAN}🔐 Configuring OpenAPI source...${NC}"

case "$SCOPE" in
    all)
        SCOPES=(public internal)
        ;;
    *)
        SCOPES=("$SCOPE")
        ;;
esac

# ============================================================================
# 3. Run Python export script (once per scope)
# ============================================================================
if [ ! -f "$PYTHON_SCRIPT" ]; then
    echo -e "${RED}Error: Export script not found: $PYTHON_SCRIPT${NC}"
    exit 1
fi

python_exe=$(command -v python3 || command -v python)

for s in "${SCOPES[@]}"; do
    echo -e "\n${CYAN}🐍 Running Postman export (scope=$s)...${NC}"

    if [ "$s" = "internal" ]; then
        OPENAPI_URL="${API_URL}/openapi/v1.internal-admin.json"
    else
        OPENAPI_URL="${API_URL}/openapi/v1.public.json"
    fi
    echo -e "${GRAY}  OpenAPI: $OPENAPI_URL (no auth)${NC}"

    "$python_exe" "$PYTHON_SCRIPT" \
        --scope "$s" \
        --openapi "$OPENAPI_URL" \
        --out-dir "$OUTPUT_DIR" \
        --local-base-url "$LOCAL_BASE_URL" \
        --dev-base-url "$DEV_BASE_URL" \
        --stage-base-url "$STAGE_BASE_URL" \
        --prod-base-url "$PROD_BASE_URL"
done

# ============================================================================
# 4. Report results
# ============================================================================
echo -e "\n${GREEN}✅ Export complete!${NC}"
echo -e "${CYAN}Output directory: $OUTPUT_DIR${NC}"

if [ -d "$OUTPUT_DIR" ]; then
    ls -lh "$OUTPUT_DIR"/BQR-*.postman_*.json 2>/dev/null | awk '{print "  📄 " $NF " (" $5 ")"}' || true
fi

echo -e "\n${CYAN}📋 Next steps:${NC}"
echo -e "  1. Import collection and environments into Postman:"
echo -e "     • Collections: $OUTPUT_DIR/BQR-*.postman_collection.json"
echo -e "     • Environments: $OUTPUT_DIR/BQR-*.postman_environment.json"
echo -e "  2. Set 'clientId' and 'clientSecret' in your environment"
echo -e "  3. Run POST /v{version}/oauth/token to get an access token"
echo -e "  4. All other requests will use the token automatically"
