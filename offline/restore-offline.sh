#!/usr/bin/env bash
# Restaurează și compilează backend-ul și frontend-ul din pachetele din offline/ (fără internet).
# Sursele sunt deja configurate: backend/NuGet.config → offline/nuget, frontend/.npmrc → offline/npm-cache.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "==> Backend"
cd "$ROOT/backend"
dotnet restore MDel.slnx
dotnet tool restore
dotnet build MDel.slnx --no-restore

echo "==> Frontend"
cd "$ROOT/frontend"
npm ci --offline
npm run build

echo "Gata."
