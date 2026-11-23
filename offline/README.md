# MDel – kit pentru dezvoltare offline

Tot ce trebuie pentru a continua dezvoltarea **fără internet**: pachetele NuGet (backend, teste, `dotnet-ef`,
pachetele pentru publicarea pe Windows), pachetele npm (frontend, inclusiv binarele pentru Windows și Linux),
istoricul git complet și, opțional, kiturile de instalare .NET și Node.js pentru Windows.

Pachetele au câteva sute de MB, de aceea **nu sunt în git**. Se generează cu un workflow GitHub Actions și se
descarcă o singură dată, pe o mașină cu internet.

## 1. Obținerea kitului (cu internet)

**Varianta A – din GitHub (recomandat, include kiturile de instalare Windows):**

1. GitHub → **Actions** → workflow-ul **Offline kit** → ultima rulare reușită
   (sau **Run workflow** pentru una nouă) → secțiunea **Artifacts** → descărcați **mdel-offline-kit**.
2. Dezarhivați conținutul arhivei **în folderul `offline/`** al repository-ului. Rezultatul:

```
offline/
  nuget/          ~300 MB  pachete NuGet (format „feed” local)
  npm-cache/      ~45 MB   cache npm
  repo/           mdel.bundle – tot istoricul git
  installers/     .NET 10 SDK (win-x64 .exe) și Node.js 22 (x64 .msi)
  README.md, restore-offline.sh, Restore-Offline.ps1, create-offline-bundle.sh
```

**Varianta B – generat local (Linux/macOS/WSL, fără kiturile de instalare Windows):**

```bash
./offline/create-offline-bundle.sh                 # doar pachetele
./offline/create-offline-bundle.sh --with-installers   # + .NET SDK și Node.js pentru Windows
```

Copiați apoi întregul repository (cu folderul `offline/` completat) pe mașina fără internet.

## 2. Pregătirea mașinii offline (o singură dată)

| Ce | De unde |
|---|---|
| .NET 10 SDK | `offline/installers/dotnet-sdk-10.*-win-x64.exe` |
| Node.js 22 (include npm) | `offline/installers/node-v22.*-x64.msi` |
| Editor | Visual Studio 2022/2026 sau VS Code (instalate separat) |
| SQL Server (pentru rulare cu bază reală) | instalare separată; altfel folosiți baza in-memory (vezi mai jos) |

Codul sursă, dacă nu aveți deja repository-ul:

```bash
git clone offline/repo/mdel.bundle MDel      # tot istoricul, cu toate branch-urile
```

## 3. Restaurarea dependențelor (fără internet)

Din rădăcina repository-ului:

```powershell
# Windows
.\offline\Restore-Offline.ps1
```

```bash
# Linux / macOS / WSL
./offline/restore-offline.sh
```

Scripturile:

1. creează `backend/NuGet.config` care folosește **doar** `offline/nuget` (cale absolută, ca să funcționeze și
   `dotnet ef migrations bundle`); fișierul nu se pune în git;
2. rulează `dotnet restore`, `dotnet tool restore` (aduce `dotnet-ef`) și `dotnet build` pentru backend;
3. rulează `npm ci --offline --cache offline/npm-cache` și `npm run build` pentru frontend.

### Manual, pas cu pas

**Backend** (din `backend/`, după ce există `backend/NuGet.config` creat de script):

```bash
dotnet restore MDel.slnx
dotnet tool restore
dotnet build MDel.slnx
dotnet test MDel.slnx
dotnet ef migrations add <Nume> -p src/MDel.Infrastructure -s src/MDel.Api -o Persistence/Migrations
dotnet ef database update -p src/MDel.Infrastructure -s src/MDel.Api
```

**Frontend** (din `frontend/`):

```bash
npm ci --offline --cache ../offline/npm-cache
npm run dev        # http://localhost:5173 (proxy /api → backend)
npm run lint
npm run build
```

**Pachetul de instalare** (offline): `scripts/publish.sh` / `scripts/Publish-MDel.ps1` rulează `npm ci`; setați
înainte variabilele npm ca să folosească doar cache-ul local:

```powershell
$env:npm_config_offline = 'true'; $env:npm_config_cache = "$PWD\offline\npm-cache"; .\scripts\Publish-MDel.ps1
```

```bash
npm_config_offline=true npm_config_cache="$PWD/offline/npm-cache" ./scripts/publish.sh
```

## 4. Rulare locală fără SQL Server, AD sau Exchange

```bash
cd backend
Database__UseInMemory=true dotnet run --project src/MDel.Api          # Linux/macOS
$env:Database__UseInMemory='true'; dotnet run --project src/MDel.Api  # PowerShell
cd ../frontend && npm run dev
```

În Development, autentificarea și integrările (AD, Exchange, SMTP) sunt simulate – vezi README-ul principal.

## 5. Ce NU este în kit

- **Browserele Playwright** pentru testele E2E (`npm run e2e`), care depind de sistemul de operare. Pe o mașină cu
  internet rulați `npx playwright install chromium` (din `frontend/`), apoi copiați folderul
  `%LOCALAPPDATA%\ms-playwright` (Windows) sau `~/.cache/ms-playwright` (Linux) în același loc pe mașina offline.
  Testele xUnit și build-ul nu au nevoie de el.
- Pachete NuGet / npm **noi**: dacă adăugați o dependență, regenerați kitul pe o mașină cu internet
  (workflow-ul *Offline kit* sau `create-offline-bundle.sh`).

## 6. Revenirea la sursele online

Ștergeți `backend/NuGet.config` (NuGet revine la nuget.org); pentru npm nu e nimic de schimbat
(fără `--offline`, npm folosește din nou registry-ul).

## Verificat

Kitul a fost testat pe o clonă nouă, cu cache-ul NuGet gol și fără acces la rețea: restore, `dotnet-ef`, build,
toate testele xUnit, pachetul de migrări (`efbundle.exe` win-x64), publicarea win-x64, `npm ci --offline` și
build-ul frontend.
