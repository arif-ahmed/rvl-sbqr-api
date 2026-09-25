# scripts/clean.ps1
# Force-clear stale IDE + MSBuild caches so Visual Studio re-evaluates
# the solution from scratch. Run this when VS's Error List disagrees
# with `dotnet build`, or after changing Directory.Build.props /
# Directory.Packages.props / global.json.
$ErrorActionPreference = 'Continue'

Write-Host "Removing bin/ and obj/ folders..." -ForegroundColor Cyan
Get-ChildItem -Path . -Recurse -Directory -Filter obj -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path . -Recurse -Directory -Filter bin -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Removing .vs/ folder..." -ForegroundColor Cyan
Remove-Item -Recurse -Force .vs -ErrorAction SilentlyContinue

Write-Host "Restoring solution..." -ForegroundColor Cyan
dotnet restore

Write-Host "Done. Close and reopen the solution in Visual Studio." -ForegroundColor Green
