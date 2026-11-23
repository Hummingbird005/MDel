# MDel – pachete pentru dezvoltare offline

Folderul conține tot ce trebuie pentru a restaura și compila proiectul **fără internet**:

```
offline/
  nuget/        ~310 MB  pachetele NuGet (backend, teste, dotnet-ef, pachetele win-x64 pentru publicare) – feed local
  npm-cache/    ~45 MB   cache npm cu pachetele frontend (inclusiv binarele native pentru Windows și Linux)
  Restore-Offline.ps1    restaurare + build pe Windows
  restore-offline.sh     restaurare + build pe Linux / macOS / WSL
  create-offline-bundle.sh   regenerează nuget/ și npm-cache/ (rulat cu internet)
```

Pachetele sunt în git, deci o simplă copie (sau clonă) a repository-ului ajunge pe mașina offline cu tot cu ele.

## 1. Pregătirea mașinii offline (o singură dată)

Kiturile de instalare **nu** sunt incluse; instalați-le separat (de pe o mașină cu internet / din kitul intern):

| Ce | Versiune |
|---|---|
| .NET SDK | 10.0 (x64) |
| Node.js (include npm) | 22 LTS (x64) |
| Editor | Visual Studio 2022/2026 sau VS Code |
| SQL Server (pentru rulare cu bază reală) | opțional – altfel baza in-memory (vezi mai jos) |

## 2. Restaurarea dependențelor (fără internet)

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

## 3. Rulare locală fără SQL Server, AD sau Exchange

```bash
cd backend
Database__UseInMemory=true dotnet run --project src/MDel.Api          # Linux/macOS
$env:Database__UseInMemory='true'; dotnet run --project src/MDel.Api  # PowerShell
cd ../frontend && npm run dev
```

În Development, autentificarea și integrările (AD, Exchange, SMTP) sunt simulate – vezi README-ul principal.

## 4. Dependențe noi

Dacă adăugați sau actualizați un pachet NuGet / npm, pe o mașină **cu internet** (Linux, macOS sau WSL):

```bash
./offline/create-offline-bundle.sh
git add offline && git commit -m "Actualizare pachete offline"
```

## 5. Ce NU este inclus

- Kiturile de instalare .NET SDK și Node.js.
- **Browserele Playwright** pentru testele E2E (`npm run e2e`). Pe o mașină cu internet rulați
  `npx playwright install chromium` (din `frontend/`), apoi copiați `%LOCALAPPDATA%\ms-playwright` (Windows) sau
  `~/.cache/ms-playwright` (Linux) în același loc pe mașina offline. Testele xUnit și build-ul nu au nevoie de ele.

## 6. Revenirea la sursele online

Ștergeți `backend/NuGet.config` (NuGet revine la nuget.org); pentru npm nu e nimic de schimbat
(fără `--offline`, npm folosește din nou registry-ul).
