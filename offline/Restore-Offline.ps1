<#
.SYNOPSIS
    Restaurează dependențele backend și frontend din pachetele din offline\ (fără internet), pe Windows.
.EXAMPLE
    .\offline\Restore-Offline.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$off = Join-Path $root 'offline'
if (-not (Test-Path (Join-Path $off 'nuget')) -or -not (Test-Path (Join-Path $off 'npm-cache'))) {
    throw 'Lipsesc offline\nuget sau offline\npm-cache – regenerați-le cu offline/create-offline-bundle.sh (vezi offline\README.md).'
}

Write-Host '==> Backend (NuGet doar din offline\nuget)'
# Cale absolută: „dotnet ef migrations bundle” compilează un proiect temporar în afara repository-ului.
$nugetFolder = (Resolve-Path (Join-Path $off 'nuget')).Path
$config = @"
<?xml version="1.0" encoding="utf-8"?>
<!-- Generat de offline\Restore-Offline.ps1: NuGet folosește doar pachetele din offline\. Ștergeți fișierul pentru surse online. -->
<configuration>
  <packageSources>
    <clear />
    <add key="mdel-offline" value="$nugetFolder" />
  </packageSources>
</configuration>
"@
Set-Content -Path (Join-Path $root 'backend\NuGet.config') -Value $config -Encoding UTF8
Push-Location (Join-Path $root 'backend')
dotnet restore MDel.slnx; if ($LASTEXITCODE) { throw 'dotnet restore failed' }
dotnet tool restore; if ($LASTEXITCODE) { throw 'dotnet tool restore failed' }
dotnet build MDel.slnx --no-restore; if ($LASTEXITCODE) { throw 'dotnet build failed' }
Pop-Location

Write-Host '==> Frontend (npm doar din offline\npm-cache)'
Push-Location (Join-Path $root 'frontend')
npm ci --offline --cache (Join-Path $off 'npm-cache') --no-audit --no-fund; if ($LASTEXITCODE) { throw 'npm ci failed' }
npm run build; if ($LASTEXITCODE) { throw 'npm run build failed' }
Pop-Location

Write-Host 'Gata. Pentru a reveni la sursele online: ștergeți backend\NuGet.config.'
