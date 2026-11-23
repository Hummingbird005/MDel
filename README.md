# MDel – forward de căsuțe email (Exchange 2016)

Aplicație prin care administratorii de sistem configurează forwardul de email
de pe căsuța unui utilizator către altul, cu aprobare, programare și expirare automată.
Planul complet: [PLAN.md](PLAN.md).

## Structură

```
backend/                     ASP.NET Core (.NET 10)
  src/MDel.Domain            entități + enum-uri (fără atribute EF)
  src/MDel.Application       interfețe: IDirectoryService, IExchangeService, IMailService, IAuditService, ICurrentUser
  src/MDel.Infrastructure    EF Core (MDelDbContext + migrații), audit, integrări (fake în etapa 0)
  src/MDel.Api               Web API, autentificare AD, middleware audit, servire SPA
  tests/MDel.Tests           xUnit (model EF, API)
frontend/                    React + TypeScript (Vite), MUI, React Query, i18next (ro/en)
```

## Baza de date (EF Core, code-first)

Tabelele, relațiile, indecșii, constrângerile și datele implicite sunt declarate în
`MDelDbContext.OnModelCreating` prin `ModelBuilder`
(`backend/src/MDel.Infrastructure/Persistence/MDelDbContext.cs`). Toate tabelele sunt în schema `mdel`.

| Tabelă | Relații |
|---|---|
| `AppUsers` | utilizatori AD (cache local) + limba preferată |
| `Delegations` | `FromUser`, `ToUser`, `CreatedBy`, `ApprovedBy`, `StoppedBy` → `AppUsers` (Restrict) |
| `DelegationAttachments` | → `Delegations` (Cascade), `UploadedBy` → `AppUsers` |
| `DelegationNotifications` | → `Delegations` (Cascade), `RecipientUser` → `AppUsers` |
| `DelegationStatusHistory` | → `Delegations` (Cascade), `ChangedBy` → `AppUsers` |
| `InformGroupMembers` | `User` → `AppUsers` (1 – 0..1, unic), `AddedBy` → `AppUsers` |
| `AppSettings` | `UpdatedBy` → `AppUsers`; valori implicite: retenție 365 zile, 100 MB etc. |
| `AuditLogs` | `User` → `AppUsers`; append-only |

Comenzile se rulează din `backend/`:

```bash
dotnet tool restore                      # instalează dotnet-ef (versiunea din dotnet-tools.json)

# după orice modificare a modelului:
dotnet ef migrations add <NumeMigrare> -p src/MDel.Infrastructure -s src/MDel.Api -o Persistence/Migrations

# creează / actualizează baza de date (connection string "MDel" din appsettings):
dotnet ef database update -p src/MDel.Infrastructure -s src/MDel.Api
```

Migrarea inițială (`InitialCreate`) este deja generată. Pentru un server de producție
fără acces direct se poate genera scriptul SQL idempotent:

```bash
dotnet ef migrations script --idempotent -p src/MDel.Infrastructure -s src/MDel.Api -o mdel.sql
```

## Rulare locală

Cerințe: .NET 10 SDK, Node 22, SQL Server.

1. Setați connection string-ul în `backend/src/MDel.Api/appsettings.Development.json`
   (sau cu `dotnet user-secrets set "ConnectionStrings:MDel" "..."`).
2. `cd backend && dotnet tool restore && dotnet ef database update -p src/MDel.Infrastructure -s src/MDel.Api`
3. `dotnet run --project src/MDel.Api` (http://localhost:5080)
4. `cd frontend && npm install && npm run dev` (http://localhost:5173, proxy `/api` → 5080)

În **Development**, autentificarea este simulată (`Authentication:Mode = Development`):
utilizatorul și grupurile AD vin din `appsettings.Development.json`, iar headerul `X-Dev-User`
permite schimbarea utilizatorului. AD, Exchange și SMTP sunt înlocuite de implementări
in-memory (`Integrations:UseFakes = true`) până la etapa 1.

## Autentificare și roluri

- Producție: Windows Authentication (Negotiate/Kerberos) pe IIS – vezi `web.config`.
- Rolurile `Admin` și `Approver` se obțin din grupurile AD configurate în `AppSettings`
  (`Groups.Admins`, `Groups.Approvers`). Cât timp acestea sunt goale, se folosesc valorile din
  `appsettings.json` (`Authentication:Groups`) – pentru primul acces al adminului.

## Teste

```bash
cd backend && dotnet test MDel.slnx
cd frontend && npm run lint && npm run build
```

Testul `Migrations_are_in_sync_with_the_model` eșuează dacă modelul s-a schimbat fără o migrare nouă.
