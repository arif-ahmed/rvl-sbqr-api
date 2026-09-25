# =============================================================================
# docker/start-db.ps1 — daily local-dev startup runbook (one command).
#
# Brings up ONLY the sbqr.postgres container (no API build, no Adminer):
#   1. Ensures the Docker engine is running (auto-starts Docker Desktop).
#   2. docker compose up -d sbqr.postgres
#   3. Waits for the healthcheck to report "healthy".
#   4. Verifies the logical databases (sbqr_app, sbqr_key_vault) exist.
#   5. Prints connection info.
#
# Idempotent: safe to re-run any time. Tables are NOT created here — schema
# is applied by the external migration tool (see docs/docker/DEPLOYMENT.md).
#
# Usage (from anywhere):
#   pwsh docker/start-db.ps1                # normal daily start
#   pwsh docker/start-db.ps1 -SkipDockerStart   # never launch Docker Desktop
# =============================================================================
#Requires -Version 7
[CmdletBinding()]
param(
    [switch]$SkipDockerStart   # Don't try to launch Docker Desktop automatically
)

$ErrorActionPreference = 'Stop'
$composeFile = Join-Path $PSScriptRoot 'docker-compose.yml'

function Test-DockerEngine {
    docker info *> $null
    return ($LASTEXITCODE -eq 0)
}

# --- 1. Ensure Docker engine is running --------------------------------------
if (-not (Test-DockerEngine)) {
    if ($SkipDockerStart) {
        Write-Error "Docker engine is not running. Start Docker Desktop, then re-run."
    }
    $dd = 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
    if (-not (Test-Path $dd)) {
        Write-Error "Docker engine is not running and Docker Desktop was not found at '$dd'."
    }
    Write-Host 'Starting Docker Desktop...' -ForegroundColor Yellow
    Start-Process $dd
    $deadline = (Get-Date).AddSeconds(120)
    while (-not (Test-DockerEngine)) {
        if ((Get-Date) -gt $deadline) {
            Write-Error 'Timed out after 120s waiting for the Docker engine.'
        }
        Start-Sleep -Seconds 5
    }
}
Write-Host 'Docker engine: running' -ForegroundColor Green

# --- 2. Start only sbqr.postgres ---------------------------------------------
Write-Host 'Starting sbqr.postgres...' -ForegroundColor Yellow
docker compose -f $composeFile up -d sbqr.postgres
if ($LASTEXITCODE -ne 0) {
    Write-Error "docker compose up failed (port conflict? see docker/.env POSTGRES_HOST_PORT)."
}

# --- 3. Wait for healthy ------------------------------------------------------
$deadline = (Get-Date).AddSeconds(60)
do {
    $status = docker inspect --format '{{.State.Health.Status}}' sbqr.postgres
    if ($status -eq 'healthy') { break }
    if ((Get-Date) -gt $deadline) {
        Write-Error "Postgres not healthy after 60s (status: $status). Check: docker compose -f $composeFile logs sbqr.postgres"
    }
    Start-Sleep -Seconds 3
} while ($true)
Write-Host 'sbqr.postgres: healthy' -ForegroundColor Green

# --- 4. Verify the databases exist --------------------------------------------
# The SBQR databases are only verified — recreating them is a destructive
# act the operator must choose deliberately.
$dbList = docker compose -f $composeFile exec -T sbqr.postgres psql -U postgres -lqt
foreach ($db in 'sbqr_app', 'sbqr_key_vault') {
    if ($dbList -match $db) {
        Write-Host "database ${db}: OK" -ForegroundColor Green
    } else {
        Write-Warning "${db} missing! The init script only runs on a fresh volume. Fix: docker compose -f $composeFile down -v ; ./docker/start-db.ps1"
    }
}

# --- 5. Summary ---------------------------------------------------------------
Write-Host ''
Write-Host 'PostgreSQL is up:' -ForegroundColor Cyan
Write-Host '  host     : localhost:5432'
Write-Host '  user/pass: postgres / postgres'
Write-Host '  databases: sbqr_app, sbqr_key_vault'
Write-Host '  psql     : docker compose -f docker/docker-compose.yml exec sbqr.postgres psql -U postgres -d sbqr_app'
Write-Host '  stop     : docker compose -f docker/docker-compose.yml down   (add -v to wipe)'
