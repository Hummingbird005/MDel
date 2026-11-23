#!/usr/bin/env bash
# Construiește pachetul de instalare MDel pentru Windows / IIS (rulează pe Linux sau în CI).
#   out/release/MDel/          – aplicația (API + interfața React în wwwroot), framework-dependent, win-x64
#   out/release/efbundle.exe   – aplică migrările: efbundle.exe --connection "<connection string>"
#   out/release/mdel-migrations.sql – script SQL idempotent (alternativă pentru DBA)
#   out/release/scripts, docs  – scripturile Exchange și documentația
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/out/release"
rm -rf "$OUT" && mkdir -p "$OUT"

echo "==> Frontend"
(cd "$ROOT/frontend" && npm ci --no-audit --no-fund && npm run build)

echo "==> Backend"
(cd "$ROOT/backend" && dotnet tool restore && \
  dotnet publish src/MDel.Api/MDel.Api.csproj -c Release -r win-x64 --self-contained false -o "$OUT/MDel" && \
  dotnet ef migrations bundle -p src/MDel.Infrastructure -s src/MDel.Api --configuration Release -r win-x64 --self-contained -o "$OUT/efbundle.exe" --force && \
  dotnet ef migrations script --idempotent -p src/MDel.Infrastructure -s src/MDel.Api -o "$OUT/mdel-migrations.sql")

echo "==> SPA în wwwroot"
mkdir -p "$OUT/MDel/wwwroot"
cp -r "$ROOT/frontend/dist/." "$OUT/MDel/wwwroot/"

# appsettings.Development.json nu are ce căuta în producție.
rm -f "$OUT/MDel/appsettings.Development.json"

cp -r "$ROOT/scripts" "$OUT/scripts"
cp -r "$ROOT/docs" "$OUT/docs"
cp "$ROOT/README.md" "$OUT/"

echo "Gata: $OUT"
