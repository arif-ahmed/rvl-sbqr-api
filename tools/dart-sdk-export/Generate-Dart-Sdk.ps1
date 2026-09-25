#!/usr/bin/env pwsh
<#
.SYNOPSIS
Generates the Dart/Flutter SDK for BQR Secure Manager's v1.public API.

.DESCRIPTION
Mirrors tools/postman-export/Generate-Postman-Export.ps1: starts the mock
TrustStore + API, fetches the live v1.public OpenAPI document, patches in
the bearer-auth security scheme and the oauth/token requestBody (see
patch-openapi-security.js for why), then runs openapi-generator-cli via
Docker (no local Java/Dart SDK required) with the dart-dio generator.

Output: sdks/dart/bqr_public_client/ (committed to the repo — this is the
artifact the mobile team consumes; see sdks/dart/README.md for usage).

.PARAMETER SkipStartup
Skip starting API/TrustStore; assume they're already running on localhost:5001/5002.

.PARAMETER SkipCleanup
Leave background processes running after generation completes.

.EXAMPLE
./tools/dart-sdk-export/Generate-Dart-Sdk.ps1

.EXAMPLE
./tools/dart-sdk-export/Generate-Dart-Sdk.ps1 -SkipStartup
#>

param(
    [switch]$SkipStartup,
    [switch]$SkipCleanup
)

$ErrorActionPreference = "Stop"

$ApiPort = 5001
$TrustStorePort = 5002
$ApiUrl = "http://localhost:$ApiPort"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..\..")
$OpenApiDir = Join-Path $RepoRoot ".openapi"
$RawSpec = Join-Path $OpenApiDir "v1.public.json"
$PatchedSpec = Join-Path $OpenApiDir "v1.public.patched.json"
$SdkOutDir = Join-Path $RepoRoot "sdks\dart\bqr_public_client"
$PatchScript = Join-Path $ScriptDir "patch-openapi-security.js"

New-Item -ItemType Directory -Force -Path $OpenApiDir | Out-Null

Write-Host "🎯 BQR Dart SDK Generator (v1.public)" -ForegroundColor Cyan

$ApiProcess = $null
$TrustStoreProcess = $null

function Cleanup {
    if (-not $SkipCleanup -and -not $SkipStartup) {
        Write-Host "`n🧹 Stopping background processes..." -ForegroundColor Yellow
        if ($ApiProcess -and -not $ApiProcess.HasExited) {
            $ApiProcess | Stop-Process -Force -ErrorAction SilentlyContinue
        }
        if ($TrustStoreProcess -and -not $TrustStoreProcess.HasExited) {
            $TrustStoreProcess | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
}
trap { Cleanup; throw $_ }

# ============================================================================
# 1. Start services (unless -SkipStartup)
# ============================================================================
if (-not $SkipStartup) {
    Write-Host "`n📦 Starting services..." -ForegroundColor Cyan

    $ApiInUse = $null -ne (Get-NetTCPConnection -LocalPort $ApiPort -ErrorAction SilentlyContinue)
    if ($ApiInUse) {
        Write-Host "  ⚠ API port $ApiPort already in use; assuming API is running" -ForegroundColor Yellow
        $SkipStartup = $true
    } else {
        Write-Host "  Starting mock TrustStore on port $TrustStorePort..." -ForegroundColor Gray
        $mockScript = @'
const http = require('http');
const server = http.createServer((req, res) => {
  if (req.url === '/trust-store/institutions' && req.method === 'GET') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ institutions: [] }));
  } else {
    res.writeHead(404, {});
    res.end('Not Found');
  }
});
server.listen(5002, '127.0.0.1', () => {
  console.log('Mock TrustStore listening on port 5002');
});
'@
        $mockPath = "$([System.IO.Path]::GetTempPath())dart-sdk-mock-trust-store.js"
        Set-Content -Path $mockPath -Value $mockScript -Force
        $TrustStoreProcess = Start-Process node -ArgumentList $mockPath -PassThru -WindowStyle Hidden -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1

        Write-Host "  Starting API on port $ApiPort..." -ForegroundColor Gray
        $ApiProcess = Start-Process `
            -FilePath dotnet `
            -ArgumentList "run", "--project", "src/Host/SBQR.Api" `
            -WorkingDirectory $RepoRoot `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput "$([System.IO.Path]::GetTempPath())dart-sdk-api-output.log" `
            -RedirectStandardError "$([System.IO.Path]::GetTempPath())dart-sdk-api-error.log"

        Write-Host "  Waiting for API to be ready..." -ForegroundColor Gray
        $maxRetries = 60
        $retries = 0
        $ready = $false
        while ($retries -lt $maxRetries) {
            try {
                $response = Invoke-WebRequest -Uri "$ApiUrl/health/live" -Method Get -TimeoutSec 2 -ErrorAction Stop
                if ($response.StatusCode -eq 200) { $ready = $true; break }
            } catch {
                $retries++
                Start-Sleep -Milliseconds 500
            }
        }
        if (-not $ready) {
            Cleanup
            throw "API failed to respond after ${maxRetries}0s. Check $([System.IO.Path]::GetTempPath())dart-sdk-api-*.log"
        }
        Write-Host "    ✓ API is ready" -ForegroundColor Green
    }
}

# ============================================================================
# 2. Fetch the live v1.public OpenAPI document
# ============================================================================
Write-Host "`n📥 Fetching OpenAPI document..." -ForegroundColor Cyan
Invoke-WebRequest -Uri "$ApiUrl/openapi/v1.public.json" -OutFile $RawSpec
Write-Host "  Saved: $RawSpec" -ForegroundColor Gray

# ============================================================================
# 3. Patch in bearer-auth + oauth/token requestBody
# ============================================================================
Write-Host "`n🔧 Patching OpenAPI document for SDK generation..." -ForegroundColor Cyan
& node $PatchScript $RawSpec $PatchedSpec
if ($LASTEXITCODE -ne 0) { throw "patch-openapi-security.js failed" }

# ============================================================================
# 4. Generate the Dart SDK via openapi-generator-cli (Docker)
# ============================================================================
Write-Host "`n🐳 Generating Dart SDK (dart-dio) via Docker..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $SdkOutDir | Out-Null

$additionalProps = "pubName=bqr_public_client,pubVersion=1.0.0," +
    "pubDescription=Dart/Flutter client for the BQR Secure Manager v1.public API (BanglaQR P2P QR generation and validation).," +
    "nullableFields=true,useEnumExtension=true"

docker run --rm `
    -v "${RepoRoot}:/local" `
    openapitools/openapi-generator-cli generate `
    -i "/local/.openapi/v1.public.patched.json" `
    -g dart-dio `
    -o "/local/sdks/dart/bqr_public_client" `
    --additional-properties=$additionalProps

if ($LASTEXITCODE -ne 0) {
    Cleanup
    throw "openapi-generator-cli failed (exit $LASTEXITCODE)"
}

Write-Host "`n✅ SDK generated: $SdkOutDir" -ForegroundColor Green
Write-Host "`n📋 Next steps:" -ForegroundColor Cyan
Write-Host "  1. Review the diff under sdks/dart/bqr_public_client/"
Write-Host "  2. Bump pubVersion in this script if the API surface changed in a breaking way"
Write-Host "  3. Commit sdks/dart/bqr_public_client/ — see sdks/dart/README.md for mobile-team usage"

Cleanup
