<#
.SYNOPSIS
    Construiește pachetul de instalare MDel pe o mașină Windows (echivalentul scripts/publish.sh).
.DESCRIPTION
    Necesită .NET 10 SDK și Node.js 22. Rezultatul ajunge în out\release:
      MDel\                 aplicația (API + interfața în wwwroot)
      efbundle.exe          aplică migrările:  .\efbundle.exe --connection "<connection string>"
      mdel-migrations.sql   script SQL idempotent (alternativă pentru DBA)
.EXAMPLE
    .\scripts\Publish-MDel.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'out\release'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

Write-Host '==> Frontend'
Push-Location (Join-Path $root 'frontend')
npm ci --no-audit --no-fund; if ($LASTEXITCODE) { throw 'npm ci failed' }
npm run build; if ($LASTEXITCODE) { throw 'frontend build failed' }
Pop-Location

Write-Host '==> Backend'
Push-Location (Join-Path $root 'backend')
dotnet tool restore; if ($LASTEXITCODE) { throw 'tool restore failed' }
dotnet publish src\MDel.Api\MDel.Api.csproj -c Release -r win-x64 --self-contained false -o (Join-Path $out 'MDel')
if ($LASTEXITCODE) { throw 'publish failed' }
# efbundle se compilează într-un folder temporar; RestoreConfigFile îl face să folosească tot offline\nuget.
$env:RestoreConfigFile = Join-Path $root 'backend\NuGet.config'
dotnet ef migrations bundle -p src\MDel.Infrastructure -s src\MDel.Api --configuration Release -r win-x64 --self-contained -o (Join-Path $out 'efbundle.exe') --force
$bundleExit = $LASTEXITCODE
Remove-Item Env:RestoreConfigFile
if ($bundleExit) { throw 'migrations bundle failed' }
dotnet ef migrations script --idempotent -p src\MDel.Infrastructure -s src\MDel.Api -o (Join-Path $out 'mdel-migrations.sql')
if ($LASTEXITCODE) { throw 'migrations script failed' }
Pop-Location

Write-Host '==> SPA în wwwroot'
$wwwroot = Join-Path $out 'MDel\wwwroot'
New-Item -ItemType Directory $wwwroot -Force | Out-Null
Copy-Item (Join-Path $root 'frontend\dist\*') $wwwroot -Recurse -Force
Remove-Item (Join-Path $out 'MDel\appsettings.Development.json') -ErrorAction SilentlyContinue

Copy-Item (Join-Path $root 'scripts') (Join-Path $out 'scripts') -Recurse
Copy-Item (Join-Path $root 'docs') (Join-Path $out 'docs') -Recurse
Copy-Item (Join-Path $root 'README.md') $out

Write-Host "Gata: $out"
