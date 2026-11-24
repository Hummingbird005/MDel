#!/usr/bin/env bash
# Regenerează pachetele pentru dezvoltare offline în folderul offline/ (rulat online, pe Linux/macOS/WSL),
# apoi se face commit la rezultat. Necesar după adăugarea sau actualizarea unei dependențe NuGet / npm.
#   offline/nuget/      – toate pachetele NuGet (backend, teste, dotnet-ef, pachetele win-x64 pentru publicare)
#   offline/npm-cache/  – cache npm cu toate pachetele frontend (inclusiv binarele Windows și Linux)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OFF="$ROOT/offline"

rm -rf "$OFF/nuget" "$OFF/npm-cache"
mkdir -p "$OFF/nuget" "$OFF/npm-cache"

echo "==> NuGet"
# backend/NuGet.config indică offline/nuget (care tocmai se regenerează): aici descărcăm de pe nuget.org.
ONLINE_CONFIG="$(mktemp -d)/NuGet.config"
cat > "$ONLINE_CONFIG" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
XML
export NUGET_PACKAGES="$OFF/nuget"   # restaurarea scrie direct în folderul offline (format „feed” local)
cd "$ROOT/backend"
dotnet restore MDel.slnx --configfile "$ONLINE_CONFIG"
# Pachetele necesare pentru publicarea pe Windows (apphost, runtime-uri pentru efbundle self-contained).
dotnet restore src/MDel.Api/MDel.Api.csproj -r win-x64 --configfile "$ONLINE_CONFIG"
dotnet restore src/MDel.Infrastructure/MDel.Infrastructure.csproj -r win-x64 --configfile "$ONLINE_CONFIG"
dotnet restore src/MDel.Api/MDel.Api.csproj -r linux-x64 --configfile "$ONLINE_CONFIG"
# efbundle self-contained are nevoie și de runtime pack-urile complete:
TMP="$(mktemp -d)"
cat > "$TMP/packs.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifiers>win-x64;linux-x64</RuntimeIdentifiers>
    <SelfContained>true</SelfContained>
  </PropertyGroup>
  <ItemGroup>
    <!-- Uneltele locale (dotnet-ef) – „dotnet tool restore” nu le copiază dacă sunt deja în cache-ul global. -->
    <PackageDownload Include="dotnet-ef" Version="[EF_VERSION]" />
  </ItemGroup>
</Project>
XML
EF_VERSION=$(python3 -c "import json;print(json.load(open('$ROOT/backend/dotnet-tools.json'))['tools']['dotnet-ef']['version'])")
sed -i "s/EF_VERSION/$EF_VERSION/" "$TMP/packs.csproj"
dotnet restore "$TMP/packs.csproj" -r win-x64 --configfile "$ONLINE_CONFIG"
dotnet restore "$TMP/packs.csproj" -r linux-x64 --configfile "$ONLINE_CONFIG"
rm -rf "$TMP"
unset NUGET_PACKAGES

# Ca sursă locală NuGet are nevoie doar de .nupkg, .nuspec și .sha512 – conținutul extras se șterge (~jumătate din spațiu).
find "$OFF/nuget" -mindepth 3 -type d -prune -exec rm -rf {} +
find "$OFF/nuget" -type f ! -name '*.nupkg' ! -name '*.nupkg.sha512' ! -name '*.nuspec' -delete

echo "==> npm (Linux + Windows)"
cd "$ROOT/frontend"
npm ci --cache "$OFF/npm-cache"
# Dependențele opționale native (esbuild, rollup) diferă pe Windows: le punem și pe ele în cache.
TMP="$(mktemp -d)"
cp package.json package-lock.json "$TMP/"
(cd "$TMP" && npm ci --cache "$OFF/npm-cache" --os=win32 --cpu=x64 --ignore-scripts --no-audit --no-fund)
rm -rf "$TMP"
npm cache verify --cache "$OFF/npm-cache" >/dev/null

# Logurile și fișierele temporare npm nu sunt necesare.
rm -rf "$OFF/npm-cache/_logs" "$OFF/npm-cache/_update-notifier-last-checked" "$OFF/npm-cache/tmp"

echo "==> Gata"
du -sh "$OFF"/nuget "$OFF"/npm-cache
