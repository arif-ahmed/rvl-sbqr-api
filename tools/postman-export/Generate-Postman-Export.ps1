#!/usr/bin/env pwsh
<#
.SYNOPSIS
Generates Postman collection + environment files for BQR Secure Manager.
Handles starting the API + mock TrustStore, waiting for readiness, and running the export.

.DESCRIPTION
Automates the full postman-export workflow without needing the /postman-export skill.
- Starts mock TrustStore on localhost:5002
- Starts API on localhost:5001
- Waits for both to be ready
- Runs the Python export script

This script is intended to be run by platform admins and the technical dev team
against a local or staging instance they already control — so the internal-admin
OpenAPI doc is fetched without HTTP Basic auth.

Configuration:
- Built-in defaults apply when tools/postman-export/.postman.env is missing.
- If tools/postman-export/.postman.env exists, KEY=VALUE pairs are loaded on top
  of the built-in defaults (POSTMAN_LOCAL_BASE_URL, POSTMAN_DEV_BASE_URL,
  POSTMAN_STAGE_BASE_URL, POSTMAN_PROD_BASE_URL).
- See tools/postman-export/.postman.env.example for the tracked template.

.PARAMETER Scope
API scope to export: "public", "internal", or "all" (default: "all").
When "all" (or omitted), generates both public and internal collections + environments.

.PARAMETER LocalBaseUrl
Base URL for Local environment. Default http://localhost:5001, overridable via
POSTMAN_LOCAL_BASE_URL in tools/postman-export/.postman.env.

.PARAMETER DevBaseUrl
Base URL for Dev environment. Default http://localhost:5001, overridable via
POSTMAN_DEV_BASE_URL in tools/postman-export/.postman.env.

.PARAMETER StageBaseUrl
Base URL for Stage environment. Default https://stage.bqr.internal, overridable via
POSTMAN_STAGE_BASE_URL in tools/postman-export/.postman.env.

.PARAMETER ProdBaseUrl
Base URL for Production environment. Default https://api.bqr.example.com, overridable via
POSTMAN_PROD_BASE_URL in tools/postman-export/.postman.env.

.PARAMETER SkipStartup
Skip starting API/TrustStore; assume they're already running.

.PARAMETER SkipCleanup
Leave background processes running after export completes.

.EXAMPLE
# Generate both public and internal exports (default)
./tools/postman-export/Generate-Postman-Export.ps1

# Generate only the public export
./tools/postman-export/Generate-Postman-Export.ps1 -Scope public

# Generate only the internal export
./tools/postman-export/Generate-Postman-Export.ps1 -Scope internal

# Override a single base URL without editing .env
./tools/postman-export/Generate-Postman-Export.ps1 -StageBaseUrl "https://stage-api.example.com"

# Skip startup (API already running elsewhere)
./tools/postman-export/Generate-Postman-Export.ps1 -Scope public -SkipStartup
#>

param(
    [ValidateSet("public", "internal", "all")]
    [string]$Scope = "all",

    [string]$LocalBaseUrl = "",
    [string]$DevBaseUrl = "",
    [string]$StageBaseUrl = "",
    [string]$ProdBaseUrl = "",

    [switch]$SkipStartup,
    [switch]$SkipCleanup
)

$ErrorActionPreference = "Stop"

$ApiPort = 5001
$TrustStorePort = 5002
$ApiUrl = "http://localhost:$ApiPort"
$TrustStoreUrl = "http://localhost:$TrustStorePort"
$OpenApiPublicUrl = "$ApiUrl/openapi/v1.public.json"
$OpenApiInternalUrl = "$ApiUrl/openapi/v1.internal-admin.json"

# Repo-relative paths anchored at the script's own directory (not CWD).
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..\..")
$PythonScript = Join-Path $RepoRoot ".claude/skills/postman-export/scripts/generate_postman.py"
$OutputDir = Join-Path $RepoRoot ".postman"

# ============================================================================
# 0. Built-in defaults, then layer .postman.env on top (if present)
# ============================================================================
$Defaults = @{
    LocalBaseUrl  = "http://localhost:5001"
    DevBaseUrl    = "http://localhost:5001"
    StageBaseUrl  = "https://stage.bqr.internal"
    ProdBaseUrl   = "https://api.bqr.example.com"
}

# Map .env keys -> canonical names
$EnvKeyToDefaultKey = @{
    "POSTMAN_LOCAL_BASE_URL"  = "LocalBaseUrl"
    "POSTMAN_DEV_BASE_URL"    = "DevBaseUrl"
    "POSTMAN_STAGE_BASE_URL"  = "StageBaseUrl"
    "POSTMAN_PROD_BASE_URL"   = "ProdBaseUrl"
}

function Load-PostmanEnv {
    $envPath = Join-Path $ScriptDir ".postman.env"
    if (-not (Test-Path $envPath)) {
        return $null
    }
    $loaded = @{}
    Get-Content -Path $envPath | ForEach-Object {
        $line = $_.Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { return }
        if ($line.StartsWith("#")) { return }
        $eq = $line.IndexOf('=')
        if ($eq -le 0) { return }
        $key = $line.Substring(0, $eq).Trim()
        $val = $line.Substring($eq + 1).Trim()
        # Strip surrounding quotes if present
        if (($val.StartsWith('"') -and $val.EndsWith('"')) -or
            ($val.StartsWith("'") -and $val.EndsWith("'"))) {
            $val = $val.Substring(1, $val.Length - 2)
        }
        $loaded[$key] = $val
    }
    return $loaded
}

$envOverrides = Load-PostmanEnv
if ($envOverrides) {
    foreach ($entry in $EnvKeyToDefaultKey.GetEnumerator()) {
        if ($envOverrides.ContainsKey($entry.Key) -and -not [string]::IsNullOrWhiteSpace($envOverrides[$entry.Key])) {
            $Defaults[$entry.Value] = $envOverrides[$entry.Key]
        }
    }
}

# Apply layer order: built-in defaults <- .env overrides <- CLI param overrides.
# An empty string from the CLI param means "no override", so the .env / default wins.
function Resolve-Value {
    param([string]$CliValue, [string]$DefaultValue)
    if (-not [string]::IsNullOrWhiteSpace($CliValue)) { return $CliValue }
    return $DefaultValue
}

$LocalBaseUrl  = Resolve-Value -CliValue $LocalBaseUrl  -DefaultValue $Defaults["LocalBaseUrl"]
$DevBaseUrl    = Resolve-Value -CliValue $DevBaseUrl    -DefaultValue $Defaults["DevBaseUrl"]
$StageBaseUrl  = Resolve-Value -CliValue $StageBaseUrl  -DefaultValue $Defaults["StageBaseUrl"]
$ProdBaseUrl   = Resolve-Value -CliValue $ProdBaseUrl   -DefaultValue $Defaults["ProdBaseUrl"]

Write-Host "🔵 BQR Postman Export Generator" -ForegroundColor Cyan
Write-Host "Scope: $Scope | Local URL: $LocalBaseUrl | Dev URL: $DevBaseUrl" -ForegroundColor Gray

# Cleanup handler for background processes
$ApiProcess = $null
$TrustStoreProcess = $null

function Cleanup {
    if (-not $SkipCleanup) {
        Write-Host "`n🧹 Stopping background processes..." -ForegroundColor Yellow
        if ($ApiProcess -and -not $ApiProcess.HasExited) {
            $ApiProcess | Stop-Process -Force -ErrorAction SilentlyContinue
            Write-Host "  ✓ Stopped API process (PID: $($ApiProcess.Id))"
        }
        if ($TrustStoreProcess -and -not $TrustStoreProcess.HasExited) {
            $TrustStoreProcess | Stop-Process -Force -ErrorAction SilentlyContinue
            Write-Host "  ✓ Stopped TrustStore process (PID: $($TrustStoreProcess.Id))"
        }
    }
}

trap {
    Cleanup
    throw $_
}

# ============================================================================
# 1. Start services (unless skipped)
# ============================================================================
if (-not $SkipStartup) {
    Write-Host "`n📦 Starting services..." -ForegroundColor Cyan

    # Check if ports are already in use
    $ApiInUse = $null -ne (Get-NetTCPConnection -LocalPort $ApiPort -ErrorAction SilentlyContinue)
    $TrustStoreInUse = $null -ne (Get-NetTCPConnection -LocalPort $TrustStorePort -ErrorAction SilentlyContinue)

    if ($ApiInUse) {
        Write-Host "  ⚠ API port $ApiPort already in use; assuming API is running" -ForegroundColor Yellow
    } else {
        Write-Host "  Starting mock TrustStore on port $TrustStorePort..." -ForegroundColor Gray
        if (-not $TrustStoreInUse) {
            # Shared mock (also runnable standalone for the Aspire AppHost
            # loop — see docs/aspire-local-dev-guide.md).
            $mockPath = "tools/postman-export/mock-trust-store.js"

            # Start Node.js in background; save process
            $TrustStoreProcess = Start-Process node -ArgumentList $mockPath -PassThru -WindowStyle Hidden -ErrorAction SilentlyContinue
            if ($TrustStoreProcess) {
                Write-Host "    ✓ TrustStore started (PID: $($TrustStoreProcess.Id))"
                Start-Sleep -Seconds 1
            } else {
                Write-Host "    ⚠ Failed to start mock TrustStore; API startup may fail" -ForegroundColor Yellow
            }
        }

        Write-Host "  Starting API on port $ApiPort..." -ForegroundColor Gray
        $ApiProcess = Start-Process `
            -FilePath dotnet `
            -ArgumentList "run", "--project", "src/Host/SBQR.Api" `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput "$([System.IO.Path]::GetTempPath())api-output.log" `
            -RedirectStandardError "$([System.IO.Path]::GetTempPath())api-error.log"
        Write-Host "    ✓ API started (PID: $($ApiProcess.Id))"

        # Wait for API to be ready
        Write-Host "  Waiting for API to be ready..." -ForegroundColor Gray
        $maxRetries = 60
        $retries = 0
        while ($retries -lt $maxRetries) {
            try {
                $response = Invoke-WebRequest -Uri "$ApiUrl/health" -Method Get -TimeoutSec 2 -ErrorAction Stop
                if ($response.StatusCode -eq 200) {
                    Write-Host "    ✓ API is ready"
                    break
                }
            } catch {
                $retries++
                if ($retries % 10 -eq 0) {
                    Write-Host "    ⏳ Still waiting... ($retries/$maxRetries)" -ForegroundColor Gray
                }
                Start-Sleep -Milliseconds 500
            }
        }

        if ($retries -ge $maxRetries) {
            Write-Host "    ⚠ API failed to respond after ${maxRetries}s" -ForegroundColor Yellow
            Write-Host "    Check $([System.IO.Path]::GetTempPath())api-*.log for details" -ForegroundColor Gray
        }
    }
}

# ============================================================================
# 2. Determine which scopes to export
# ============================================================================
function Resolve-Scopes {
    param([string]$RequestedScope)
    if ($RequestedScope -eq "all") {
        return @("public", "internal")
    }
    return @($RequestedScope)
}

$scopes = Resolve-Scopes -RequestedScope $Scope
Write-Host "`n🔐 Configuring OpenAPI source..." -ForegroundColor Cyan
Write-Host "  Scopes to export: $($scopes -join ', ')" -ForegroundColor Gray

# ============================================================================
# 3. Run Python export script (once per scope)
# ============================================================================
function Invoke-PythonExport {
    param(
        [string]$ScopeName,
        [string]$OpenApiUrl,
        [string]$LocalBase,
        [string]$DevBase,
        [string]$StageBase,
        [string]$ProdBase
    )

    Write-Host "`n🐍 Running Postman export (scope=$ScopeName)..." -ForegroundColor Cyan

    if (-not (Test-Path $PythonScript)) {
        throw "Export script not found: $PythonScript"
    }

    $pythonArgs = @(
        $PythonScript,
        "--scope", $ScopeName,
        "--openapi", $OpenApiUrl,
        "--out-dir", $OutputDir,
        "--local-base-url", $LocalBase,
        "--dev-base-url", $DevBase,
        "--stage-base-url", $StageBase,
        "--prod-base-url", $ProdBase
    )

    # Find Python - prefer "python" on Windows, then "py", then "python3"
    $pythonExe = $null
    foreach ($candidate in @("python", "py", "python3")) {
        try {
            & $candidate --version 2>&1 | Out-Null
            $pythonExe = $candidate
            break
        } catch {
            # Try next candidate
        }
    }

    if (-not $pythonExe) {
        Write-Host "`n❌ Python not found!" -ForegroundColor Red
        Write-Host "Install from: https://www.python.org/downloads/" -ForegroundColor Yellow
        exit 1
    }

    & $pythonExe @pythonArgs

    if ($LASTEXITCODE -ne 0) {
        throw "Python export script failed with scope=$ScopeName (exit code $LASTEXITCODE)"
    }
}

foreach ($s in $scopes) {
    if ($s -eq "internal") {
        $openApi = $OpenApiInternalUrl
    } else {
        $openApi = $OpenApiPublicUrl
    }
    Write-Host "  • $s -> $openApi (no auth)" -ForegroundColor Gray

    Invoke-PythonExport `
        -ScopeName $s `
        -OpenApiUrl $openApi `
        -LocalBase $LocalBaseUrl `
        -DevBase $DevBaseUrl `
        -StageBase $StageBaseUrl `
        -ProdBase $ProdBaseUrl
}

# ============================================================================
# 4. Report results
# ============================================================================
Write-Host "`n✅ Export complete!" -ForegroundColor Green
Write-Host "Output directory: $OutputDir" -ForegroundColor Cyan

if (Test-Path $OutputDir) {
    Get-ChildItem $OutputDir -Filter "BQR-*.postman_*.json" | ForEach-Object {
        $size = $_.Length / 1KB
        Write-Host "  📄 $($_.Name) ($([math]::Round($size, 2)) KB)" -ForegroundColor Gray
    }
}

Write-Host "`n📋 Next steps:" -ForegroundColor Cyan
Write-Host "  1. Import collection and environments into Postman:"
Write-Host "     • Collections: $OutputDir/BQR-*.postman_collection.json"
Write-Host "     • Environments: $OutputDir/BQR-*.postman_environment.json"
Write-Host "  2. Set 'clientId' and 'clientSecret' in your environment"
Write-Host "  3. Run POST /v{version}/oauth/token to get an access token"
Write-Host "  4. All other requests will use the token automatically"

# Cleanup
Cleanup
