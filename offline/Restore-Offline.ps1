<#
.SYNOPSIS
    Restaurează și compilează backend-ul și frontend-ul din pachetele din offline\ (fără internet), pe Windows.
.DESCRIPTION
    Sursele sunt deja configurate: backend\NuGet.config -> offline\nuget, frontend\.npmrc -> offline\npm-cache.
.EXAMPLE
    .\offline\Restore-Offline.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Write-Host '==> Backend'
Push-Location (Join-Path $root 'backend')
dotnet restore MDel.slnx; if ($LASTEXITCODE) { throw 'dotnet restore failed' }
dotnet tool restore; if ($LASTEXITCODE) { throw 'dotnet tool restore failed' }
dotnet build MDel.slnx --no-restore; if ($LASTEXITCODE) { throw 'dotnet build failed' }
Pop-Location

Write-Host '==> Frontend'
Push-Location (Join-Path $root 'frontend')
npm ci --offline; if ($LASTEXITCODE) { throw 'npm ci failed' }
npm run build; if ($LASTEXITCODE) { throw 'npm run build failed' }
Pop-Location

Write-Host 'Gata.'
