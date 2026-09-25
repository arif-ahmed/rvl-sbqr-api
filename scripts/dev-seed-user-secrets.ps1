# dev-seed-user-secrets.ps1 - one-shot helper to populate `dotnet user-secrets`
# for the SBQR.Api project after a fresh clone (Windows PowerShell equivalent
# of dev-seed-user-secrets.sh).
#
# Usage:
#   .\scripts\dev-seed-user-secrets.ps1
#
# After running this, `dotnet run --project src/Host/SBQR.Api` (with
# ASPNETCORE_ENVIRONMENT=Development, the default on a developer machine)
# resolves every connection string, signing key, and trust-store URL the
# host needs. Re-running is idempotent - `user-secrets set` overwrites
# existing values.
#
# Production / on-prem deployments MUST NOT use this script - production
# secrets are injected via environment variables by the host orchestrator
# (systemd EnvironmentFile, K8s env:, Windows service environment, ...).
# See AGENTS.md §"Non-Negotiable Constraints" / C3 + C9 and the README
# "Secrets" section.
#
# What this script seeds:
#   - Two local Postgres connection strings (matches docker-compose defaults).
#   - A DEV-ONLY HS256 JWT signing key (32+ chars). The host refuses this
#     key in Production (Program.cs §10a) - do not reuse it in any
#     environment beyond local development.
#   - The trust-store endpoint (TrustStore:BaseUrl) the host syncs from.
#   - Dev S3 vault selection + the shared dev bucket coordinates (region /
#     bucket / folder - non-secret). The base appsettings.json defaults
#     Crypto:VaultProvider to Local; dev overrides it back to S3 here.
#   - One-time bridge: if a legacy appsettings.Local.json still exists next
#     to the project, its Storage credentials are migrated into user-secrets
#     (never echoed) so the file can be deleted.
#
# What this script deliberately does NOT seed:
#   - Auth:Bootstrap:ClientSecretHash - an Argon2id PHC string that must be
#     PERSONAL per developer (rotation hygiene). Generate once with:
#         dotnet run --project src/Host/SBQR.Api -- --generate-bootstrap-secret

[CmdletBinding()]
param(
    [string]$Project = "src/Host/SBQR.Api/SBQR.Api.csproj"
)

$ErrorActionPreference = 'Stop'

# DEV-ONLY Postgres connection strings. Mirrors docker-compose.yml -
# these are the canonical dev infra defaults (localhost:5432,
# postgres/postgres). They are NOT real secrets; they are documented
# disposable values that match the docker-compose stack.
$sbqrAppCs        = 'Host=localhost;Port=5432;Database=sbqr_app;Username=postgres;Password=postgres;Include Error Detail=true'
$sbqrKeyVaultCs   = 'Host=localhost;Port=5432;Database=sbqr_key_vault;Username=postgres;Password=postgres;Include Error Detail=true'

# DEV-ONLY HS256 signing key (32+ chars). Mirrors docker-compose.yml line
# 180 - known disposable dev value across the project. Do not use in
# Production; the host refuses it at startup in Production (Program.cs §10a).
$jwtSigningKey    = 'dev-only-signing-key-change-me-0123456789abcdef'

# Trust-store endpoint the host syncs from. Point at whatever trust store you
# test against — e.g. the ephemeral Node mock from tools/postman-export
# (port 5002). A dead endpoint only logs sync errors; the host still boots.
$trustStoreBase   = 'http://localhost:5002'

# Dev S3 vault + shared dev-bucket coordinates (NON-secret - shared team
# values, match docs/dev-s3-guide.md). Personal AWS credentials are never
# seeded here; they migrate from legacy appsettings.Local.json below or are
# set manually.
$cryptoVault      = 'S3'
$storageRegion    = 'ap-southeast-1'
$storageBucket    = 'sbqr-dev-key-vault'
$storageFolder    = 'keycustody'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "dotnet CLI not found in PATH" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $Project)) {
    Write-Host "Project file not found: $Project" -ForegroundColor Red
    Write-Host "Run this from the repo root."
    exit 1
}

Write-Host "SBQR.Api dev secrets seeder" -ForegroundColor Cyan
Write-Host "project: $Project" -ForegroundColor DarkGray
Write-Host ""

$entries = @(
    @{ Key = 'ConnectionStrings:sbqr_app';      Value = $sbqrAppCs      },
    @{ Key = 'ConnectionStrings:sbqr_key_vault'; Value = $sbqrKeyVaultCs },
    @{ Key = 'Jwt:SigningKey';                   Value = $jwtSigningKey  },
    @{ Key = 'TrustStore:BaseUrl';               Value = $trustStoreBase },
    # Dev-only cadence: sync the trust directory once at boot and every
    # minute after. Staging/prod never set these (silent-on-startup is the
    # default).
    @{ Key = 'TrustStore:SyncOnStartup';         Value = 'true'          },
    @{ Key = 'TrustStore:SyncIntervalMinutes';   Value = '1'             },
    # Dev vault/backend selection + shared bucket coordinates.
    @{ Key = 'Crypto:VaultProvider';             Value = $cryptoVault    },
    @{ Key = 'Storage:Region';                   Value = $storageRegion  },
    @{ Key = 'Storage:BucketName';               Value = $storageBucket  },
    @{ Key = 'Storage:VaultFolder';              Value = $storageFolder  }
)

foreach ($e in $entries) {
    Write-Host ("-> " + $e.Key) -ForegroundColor Yellow
    & dotnet user-secrets set $e.Key $e.Value --project $Project | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host ("Failed to set " + $e.Key) -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# ---------------------------------------------------------------------------
# One-time bridge: migrate personal AWS credentials from the legacy
# appsettings.Local.json (if still present) into user-secrets. Values are
# passed as variables and never echoed. After migration the legacy file is
# unused and safe to delete.
# ---------------------------------------------------------------------------
$localJson = Join-Path (Split-Path $Project -Parent) 'appsettings.Local.json'
$existing  = (& dotnet user-secrets list --project $Project 2>$null) -join "`n"
$needCreds = $existing -notmatch 'Storage:AccessKeyId'

if ($needCreds -and (Test-Path $localJson)) {
    $local = Get-Content $localJson -Raw | ConvertFrom-Json
    if ($local.Storage.AccessKeyId -and $local.Storage.SecretAccessKey) {
        Write-Host "-> Storage:AccessKeyId / Storage:SecretAccessKey (migrated from appsettings.Local.json)" -ForegroundColor Yellow
        & dotnet user-secrets set 'Storage:AccessKeyId'  $local.Storage.AccessKeyId  --project $Project | Out-Null
        & dotnet user-secrets set 'Storage:SecretAccessKey' $local.Storage.SecretAccessKey --project $Project | Out-Null
        Write-Host "   Legacy appsettings.Local.json can now be deleted." -ForegroundColor DarkGray
        $needCreds = $false
    }
}

if ($needCreds) {
    Write-Host ""
    Write-Host "Personal AWS credentials not found in user-secrets." -ForegroundColor Yellow
    Write-Host "Set them manually (they are personal; never committed):" -ForegroundColor Yellow
    Write-Host ("  dotnet user-secrets set `"Storage:AccessKeyId`" `"<aws key id>`" --project " + $Project) -ForegroundColor Cyan
    Write-Host ("  dotnet user-secrets set `"Storage:SecretAccessKey`" `"<aws secret>`" --project " + $Project) -ForegroundColor Cyan
}

Write-Host ""
Write-Host "Seeded 10 dev secrets into the per-user store." -ForegroundColor Green
Write-Host ""
Write-Host "One more key must be generated individually (one-time, personal):" -ForegroundColor Yellow
Write-Host "  - Auth:Bootstrap:ClientSecretHash - Argon2id PHC." -ForegroundColor Cyan
Write-Host "      dotnet run --project src/Host/SBQR.Api -- --generate-bootstrap-secret"
Write-Host "      dotnet user-secrets set `"Auth:Bootstrap:ClientSecretHash`" `"<phc>`" --project $Project"
Write-Host ""
Write-Host "Verify with: dotnet user-secrets list --project $Project" -ForegroundColor DarkGray
