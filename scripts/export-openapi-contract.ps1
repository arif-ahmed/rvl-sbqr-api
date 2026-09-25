# export-openapi-contract.ps1 — regenerate contracts/v1.public.json (PF-1).
#
# Usage:
#   ./scripts/export-openapi-contract.ps1          # default port 5108
#   $env:SBQR_CONTRACT_PORT = '5200'; ./scripts/...
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
# scripts/dev-seed-user-secrets.ps1, or a repo-root .env). No database is
# required — nothing on this path touches Postgres or the vault; the
# trust-store startup sync will log connection errors and continue.
#
# When to run it:
#   Whenever a v1.public API surface changes (new/changed controller,
#   route, DTO, or response shape). Commit the regenerated artifact with
#   the API change so downstream consumers (the FI gateway pins this file
#   for DTO codegen and drift CI) see the contract move in the same PR.

$ErrorActionPreference = 'Stop'

$port = if ($env:SBQR_CONTRACT_PORT) { $env:SBQR_CONTRACT_PORT } else { '5108' }
$baseUrl = "http://127.0.0.1:${port}"
$project = 'src/Host/SBQR.Api/SBQR.Api.csproj'
$hostDll = 'src/Host/SBQR.Api/bin/Debug/net10.0/SBQR.Api.dll'
$contract = 'contracts/v1.public.json'
$capture = "$contract.capture"
$log = 'sbqr-contract-export.log'

Write-Host "[1/4] Building SBQR.Api (Debug)..."
dotnet build $project --configuration Debug --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "build failed" }

Write-Host "[2/4] Booting host on $baseUrl (Development, loopback only)..."
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = $baseUrl
# ASPNETCORE_CONTENTROOT: running the DLL directly would otherwise treat the
# CWD as content root and fail to find appsettings.json (and the repo-root
# .env loader walks ../../.. from the content root, which must be the
# project directory, same as `dotnet run`).
$env:ASPNETCORE_CONTENTROOT = (Resolve-Path 'src/Host/SBQR.Api').Path
$hostProcess = Start-Process -FilePath 'dotnet' -ArgumentList $hostDll `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err" -PassThru -NoNewWindow
try {
    $ready = $false
    for ($i = 0; $i -lt 90; $i++) {
        try {
            Invoke-RestMethod -Uri "$baseUrl/health/live" -TimeoutSec 2 | Out-Null
            $ready = $true
            break
        } catch {
            Start-Sleep -Seconds 1
        }
    }
    if (-not $ready) { throw "host did not become healthy within 90s — see $log" }

    Write-Host "[3/4] Capturing /openapi/v1.public.json ..."
    Invoke-WebRequest -Uri "$baseUrl/openapi/v1.public.json" -OutFile $capture | Out-Null

    Write-Host "[4/4] Validating and publishing $contract ..."
    New-Item -ItemType Directory -Force -Path (Split-Path $contract) | Out-Null
    # The shared normalizer (also used by the sh variant) validates the
    # capture and strips the environment-specific `servers` block so the
    # published artifact is byte-identical regardless of which machine ran
    # the export.
    dotnet run scripts/normalize-openapi-artifact.cs $capture $contract
    if ($LASTEXITCODE -ne 0) { throw "normalization failed" }
    Remove-Item -Force $capture -ErrorAction SilentlyContinue
    Write-Host ("Exported: {0} ({1} bytes)" -f $contract, (Get-Item $contract).Length)
} finally {
    if (-not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id -Force
    }
    $hostProcess.WaitForExit()
}
