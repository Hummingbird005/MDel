# MDel – rulare locală: backend din Visual Studio, frontend din cmd

Pentru testarea funcționalității pe calculatorul de dezvoltare, înainte de IIS. Backend-ul rulează în mediul
**Development**: login simulat (fără IIS / Windows Authentication), AD real după ce îl configurați în Setări, iar Exchange
și emailul sunt implicit simulate.

## 1. Ce trebuie instalat

| Ce | Observații |
|---|---|
| Visual Studio 2026 (sau 2022 actualizat) cu workload-ul **ASP.NET and web development** | include **SQL Server Express LocalDB** |
| .NET 10 SDK | vine cu Visual Studio sau din `offline/` (vezi [offline/README.md](../offline/README.md)) |
| Node.js 22 | pentru frontend |
| Acces la un controler de domeniu | pentru AD real: portul 636 (LDAPS) sau 389 de pe calculatorul dvs. |

Pachetele NuGet și npm sunt în repository (`offline/`), deci restore-ul merge și fără internet.

## 2. Configurare locală: `backend/src/MDel.Api/appsettings.Local.json`

Creați fișierul **`backend\src\MDel.Api\appsettings.Local.json`** (lângă `appsettings.json`). Se citește doar în
Development, suprascrie `appsettings.Development.json` și **e ignorat de git**, deci nu intră în conflict la actualizări și
nu ajunge în repository. Conținut minim:

```json
{
  "ConnectionStrings": {
    "MDel": "Server=(localdb)\\MSSQLLocalDB;Database=MDel_Dev;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Authentication": {
    "Development": { "User": "CORP\\ion.popescu" },
    "Groups": { "Admins": "", "Approvers": "", "SystemAdmins": "" }
  },
  "Setup": {
    "InitialAdmins": [ "CORP\\ion.popescu" ]
  }
}
```

| Cheie | Ce face local |
|---|---|
| `ConnectionStrings:MDel` | LocalDB: baza `MDel_Dev` se creează singură la prima pornire. Alt SQL Server: `Server=NUME\\SQLEXPRESS;Database=MDel_Dev;Trusted_Connection=True;TrustServerCertificate=True`. Fără SQL deloc: adăugați `"Database": { "UseInMemory": true }` (datele se pierd la oprire). |
| `Authentication:Development:User` | utilizatorul cu care „sunteți logat”: puneți **contul dvs. din AD** (numele și emailul se iau din AD, după ce îl configurați). |
| `Authentication:Groups` (goale) | dezactivează grupurile implicite de test din `appsettings.Development.json`; fără grupuri configurate, rolurile vin din `InitialAdmins`. |
| `Setup:InitialAdmins` | **contul dvs. primește toate cele trei roluri** (Administrator delegări, Aprobator, Administrator sistem) cât timp grupurile lor nu sunt completate în Setări. |

**Active Directory, Exchange, SMTP și grupurile se configurează din aplicație** (pasul 5), nu din fișier. Nu puneți
conturi sau parole în niciun fișier: se salvează criptat în baza de date. Exchange și emailul sunt implicit **simulate**
(`Integrations:UseFakes` din `appsettings.Development.json`); AD-ul este simulat doar până completați serverul sau
domeniul în Setări → Active Directory, apoi se folosește cel real, fără repornire. Separat se pot forța:
`"Integrations": { "FakeExchange": false, "FakeMail": false, "FakeDirectory": false }`.

## 3. Backend din Visual Studio

1. Deschideți `backend\MDel.slnx`.
2. Proiect de pornire: **MDel.Api** (click dreapta → *Set as Startup Project*).
3. Profilul de rulare: **http** (lista de lângă butonul ▶).
4. **F5** (sau Ctrl+F5). În fereastra de consolă apar, la prima pornire:
   `Applying 4 database migration(s)…`, `Database migrations applied.`, `Initial settings applied: …`.
5. Verificare în browser: <http://localhost:5080/api/health/ready> → `"status":"ready"`.

Dacă pornirea se oprește cu eroare de bază de date: LocalDB nu e instalat sau connection string-ul e greșit (vezi tabelul).

## 4. Frontend din cmd

```cmd
cd C:\cale\catre\MDel\frontend
npm ci
npm run dev
```

Deschideți <http://localhost:5173>. Interfața trimite cererile `/api` către backend-ul de pe portul 5080.
Backend-ul trebuie să ruleze (pasul 3).

## 5. Ce testați și cum

1. **Setări → Active Directory** (cu contul din `InitialAdmins`): completați domeniul, serverul (ex. `dc01.corp.local`),
   „Folosește SSL (LDAPS)” (port 636), OU-urile de căutare (unul pe linie), contul de citire și parola → **Salvează** →
   **Testează conexiunea**. Trebuie să arate serverul, contul și fiecare OU cu „OK”; la eroare, mesajul spune cauza
   (cont sau parolă, port, certificat, OU). Contul și parola se salvează criptat și nu se mai afișează.
2. **Setări → Grupuri**: completați grupurile AD ale celor trei roluri. Regula adminului inițial: pentru fiecare rol,
   contul din `InitialAdmins` îl are cât timp grupul lui e gol. Completați **ultimul** grupul administratorilor de sistem, după
   ce v-ați adăugat în el (altfel vă pierdeți accesul la Setări).
3. **Delegare nouă**: căutați sursa și destinatarul (vin din AD-ul real, doar din OU-urile configurate), date de
   început/sfârșit, fișier atașat.
4. **Aprobare cu alt utilizator**: deschideți în browser (înlocuiți cu un cont real din AD):

   ```
   http://localhost:5173/api/dev/login-as?user=dan.georgescu&groups=MDel-Approvers
   ```

   Sunteți acum `dan.georgescu`, cu rol de aprobator (dacă grupul `MDel-Approvers` e completat în Setări cu acest nume);
   mergeți la **Aprobări**. Alte exemple:
   - `?user=maria.ionescu&groups=none` – utilizator fără rol (trebuie să vadă „Nu aveți drepturi”);
   - `?user=ion.popescu&groups=MDel-Admins,MDel-System` – administrator de delegări și de sistem;
   - fără parametri (`/api/dev/login-as`) – înapoi la utilizatorul din `appsettings.Local.json`.
5. **Activare / expirare**: jobul automat rulează la 30 de secunde; o delegare cu început „acum” devine activă în
   cel mult 30 s, iar la ora de sfârșit expiră. Cu Exchange simulat, forward-ul nu ajunge pe căsuțe.
6. **Emailuri**: Setări → Notificări email arată ce s-ar fi trimis (destinatar, subiect, stare).
7. **Audit**: Istoric / Audit arată toate acțiunile.

Checklist-ul complet: [UAT.md](UAT.md) (pașii cu Exchange real se fac pe mediul de test, cu `FakeExchange: false`
sau pe IIS).

## 6. Login real cu Windows (opțional)

Login-ul simulat e doar pentru dezvoltare. Autentificarea Windows reală (Kerberos/NTLM) se verifică pe IIS
(vezi [DEPLOY.md](DEPLOY.md)); prin serverul de dezvoltare al frontend-ului (portul 5173) NTLM nu funcționează.

## Probleme frecvente

| Simptom | Cauză probabilă |
|---|---|
| `npm run dev` pornește, dar pagina arată „Nu se poate încărca” / erori 502 | backend-ul nu rulează pe 5080 (profilul **http** din Visual Studio) |
| Căutarea de utilizatori găsește doar „Dev Admin”, „Ion Popescu” | AD simulat: serverul și domeniul nu sunt completate în Setări → Active Directory |
| Nu văd Setări / Audit | contul nu are rolul Administrator sistem: verificați `Setup:InitialAdmins` în `appsettings.Local.json`, iar dacă grupul de sistem e deja completat în Setări, contul trebuie să fie membru |
| Vedeam Setări, dar după ce am completat grupurile nu mai văd | grupul administratorilor de sistem nu vă conține: `/api/dev/login-as?user=contul-dvs&groups=<grupul-de-sistem>` sau goliți-l din baza `MDel_Dev` |
| Testul AD: „cannot reach … over SSL” | portul 636 blocat de pe calculatorul dvs. sau certificatul DC-ului nu e de încredere; încercați temporar `"ValidateCertificate": false` |
| Eroare la pornire despre SQL / LocalDB | LocalDB lipsește: Visual Studio Installer → Individual components → *SQL Server Express LocalDB*, sau folosiți `Database:UseInMemory` |
| Încărcarea fișierelor eșuează | `Attachments.StoragePath` lipsă sau folderul nu poate fi creat (Setări → Fișiere → Testează) |
