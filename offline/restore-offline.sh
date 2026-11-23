#!/usr/bin/env bash
# Restaurează dependențele backend și frontend din pachetele din offline/ (fără internet). Rulați din rădăcina repository-ului.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OFF="$ROOT/offline"
[[ -d "$OFF/nuget" && -d "$OFF/npm-cache" ]] || { echo "Lipsesc offline/nuget sau offline/npm-cache – regenerați-le cu offline/create-offline-bundle.sh (vezi offline/README.md)."; exit 1; }

echo "==> Backend (NuGet doar din offline/nuget)"
# Cale absolută: „dotnet ef migrations bundle” compilează un proiect temporar în afara repository-ului.
cat > "$ROOT/backend/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<!-- Generat de offline/restore-offline.sh: NuGet folosește doar pachetele din offline/. Ștergeți fișierul pentru surse online. -->
<configuration>
  <packageSources>
    <clear />
    <add key="mdel-offline" value="$OFF/nuget" />
  </packageSources>
</configuration>
XML
cd "$ROOT/backend"
dotnet restore MDel.slnx
dotnet tool restore
dotnet build MDel.slnx --no-restore

echo "==> Frontend (npm doar din offline/npm-cache)"
cd "$ROOT/frontend"
npm ci --offline --cache "$OFF/npm-cache" --no-audit --no-fund
npm run build

echo "Gata. Pentru a reveni la sursele online: ștergeți backend/NuGet.config."
