#!/usr/bin/env bash
# Creează kitul pentru dezvoltare offline în folderul offline/ (rulat online, pe Linux sau în CI):
#   offline/nuget/        – toate pachetele NuGet (backend, teste, dotnet-ef, pachetele win-x64 pentru publicare)
#   offline/npm-cache/    – cache npm cu toate pachetele frontend (inclusiv binarele Windows și Linux)
#   offline/repo/mdel.bundle – tot istoricul git (clonare fără GitHub)
#   offline/installers/   – opțional (--with-installers): .NET 10 SDK și Node.js 22 pentru Windows x64
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OFF="$ROOT/offline"
WITH_INSTALLERS=false
[[ "${1:-}" == "--with-installers" ]] && WITH_INSTALLERS=true

rm -rf "$OFF/nuget" "$OFF/npm-cache" "$OFF/repo" "$OFF/installers"
mkdir -p "$OFF/nuget" "$OFF/npm-cache" "$OFF/repo"

echo "==> NuGet"
export NUGET_PACKAGES="$OFF/nuget"   # restaurarea scrie direct în folderul offline (format „feed” local)
cd "$ROOT/backend"
dotnet restore MDel.slnx
# Pachetele necesare pentru publicarea pe Windows (apphost, runtime-uri pentru efbundle self-contained).
dotnet restore src/MDel.Api/MDel.Api.csproj -r win-x64
dotnet restore src/MDel.Infrastructure/MDel.Infrastructure.csproj -r win-x64
dotnet restore src/MDel.Api/MDel.Api.csproj -r linux-x64
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
dotnet restore "$TMP/packs.csproj" -r win-x64
dotnet restore "$TMP/packs.csproj" -r linux-x64
rm -rf "$TMP"
unset NUGET_PACKAGES

# Ca sursă locală NuGet are nevoie doar de .nupkg, .nuspec și .sha512 – conținutul extras se șterge (~jumătate din spațiu).
find "$OFF/nuget" -mindepth 3 -type d -prune -exec rm -rf {} +
find "$OFF/nuget" -type f ! -name '*.nupkg' ! -name '*.nupkg.sha512' ! -name '*.nuspec' -delete

echo "==> npm (Linux + Windows)"
cd "$ROOT/frontend"
npm ci --cache "$OFF/npm-cache" --no-audit --no-fund
# Dependențele opționale native (esbuild, rollup) diferă pe Windows: le punem și pe ele în cache.
TMP="$(mktemp -d)"
cp package.json package-lock.json "$TMP/"
(cd "$TMP" && npm ci --cache "$OFF/npm-cache" --os=win32 --cpu=x64 --ignore-scripts --no-audit --no-fund)
rm -rf "$TMP"
npm cache verify --cache "$OFF/npm-cache" >/dev/null

echo "==> git bundle"
cd "$ROOT"
git bundle create "$OFF/repo/mdel.bundle" --all

if $WITH_INSTALLERS; then
  echo "==> Kituri de instalare Windows"
  mkdir -p "$OFF/installers"
  SDK_URL=$(curl -fsSL https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json |
    python3 -c "import sys,json;r=json.load(sys.stdin)['releases'][0];print(next(f['url'] for f in r['sdk']['files'] if f['rid']=='win-x64' and f['name'].endswith('.exe')))")
  curl -fsSL -o "$OFF/installers/$(basename "$SDK_URL")" "$SDK_URL"
  NODE_MSI=$(curl -fsSL https://nodejs.org/dist/latest-v22.x/SHASUMS256.txt | awk '/-x64\.msi$/{print $2}')
  curl -fsSL -o "$OFF/installers/$NODE_MSI" "https://nodejs.org/dist/latest-v22.x/$NODE_MSI"
fi

echo "==> Gata"
du -sh "$OFF"/nuget "$OFF"/npm-cache "$OFF"/repo ${WITH_INSTALLERS:+"$OFF/installers"} 2>/dev/null || true
