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
Sursele sunt **deja configurate** în repository, nu trebuie generat nimic:

| Fișier | Ce face |
|---|---|
| `backend/NuGet.config` | NuGet folosește doar `offline/nuget` (cale relativă la fișier) |
| `frontend/.npmrc` | npm folosește cache-ul `offline/npm-cache` (`prefer-offline`) |

## 1. Pregătirea mașinii offline (o singură dată)

Kiturile de instalare **nu** sunt incluse; instalați-le separat (de pe o mașină cu internet / din kitul intern):

| Ce | Versiune |
|---|---|
| .NET SDK | 10.0 (x64) |
| Node.js (include npm) | 22 LTS (x64) |
| Editor | Visual Studio 2022/2026 sau VS Code |
| SQL Server (pentru rulare cu bază reală) | opțional – altfel baza in-memory (vezi mai jos) |

## 2. Restaurarea dependențelor (fără internet)

Doar comenzile obișnuite:

```bash
(cd backend  && dotnet restore MDel.slnx && dotnet tool restore)
(cd frontend && npm ci)
```

sau, din rădăcina repository-ului, scriptul care face restore + build pentru amândouă:

```powershell
.\offline\Restore-Offline.ps1        # Windows
```

```bash
./offline/restore-offline.sh         # Linux / macOS / WSL
```

### Manual, pas cu pas

**Backend** (din `backend/`):

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
npm ci             # din offline/npm-cache (frontend/.npmrc); --offline = fără nicio încercare de rețea
npm run dev        # http://localhost:5173 (proxy /api → backend)
npm run lint
npm run build
```

**Pachetul de instalare** (offline): `scripts/Publish-MDel.ps1` / `scripts/publish.sh` merg direct; pentru
`efbundle.exe` setează singure `RestoreConfigFile`. Rulat manual, `dotnet ef migrations bundle` are nevoie de ea, pentru
că se compilează într-un folder temporar, unde calea relativă din `NuGet.config` nu mai e validă:

```powershell
$env:RestoreConfigFile = "$PWD\NuGet.config"     # din backend\
dotnet ef migrations bundle -p src\MDel.Infrastructure -s src\MDel.Api -r win-x64 --self-contained -o efbundle.exe
```

Baza de date nu mai are nevoie de `efbundle` la instalare: aplicația o creează la prima pornire.

## 3. Rulare locală fără SQL Server, AD sau Exchange

```bash
cd backend
Database__UseInMemory=true dotnet run --project src/MDel.Api          # Linux/macOS
$env:Database__UseInMemory='true'; dotnet run --project src/MDel.Api  # PowerShell
cd ../frontend && npm run dev
```

În Development, autentificarea și integrările (AD, Exchange, SMTP) sunt simulate – vezi README-ul principal.

## 4. Dependențe noi

Pe o mașină **cu internet** (Linux, macOS sau WSL), după ce ați adăugat pachetul în `.csproj` / `package.json`:

```bash
./offline/create-offline-bundle.sh      # descarcă de pe nuget.org / npmjs.org, ignorând NuGet.config din repo
git add offline && git commit -m "Actualizare pachete offline"
```

Pe mașina offline, `dotnet add package` / `npm install <pachet>` funcționează doar pentru pachete deja în `offline/`.

## 5. Ce NU este inclus

- Kiturile de instalare .NET SDK și Node.js.
- **Browserele Playwright** pentru testele E2E (`npm run e2e`). Pe o mașină cu internet rulați
  `npx playwright install chromium` (din `frontend/`), apoi copiați `%LOCALAPPDATA%\ms-playwright` (Windows) sau
  `~/.cache/ms-playwright` (Linux) în același loc pe mașina offline. Testele xUnit și build-ul nu au nevoie de ele.

## 6. Sursele online (opțional)

Pentru un restore direct de pe internet, fără `offline/`:

```bash
dotnet restore MDel.slnx --source https://api.nuget.org/v3/index.json
npm ci --prefer-online
```
