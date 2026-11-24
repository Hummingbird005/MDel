# MDel – rulare locală: backend din Visual Studio, frontend din cmd

Pentru testarea funcționalității pe calculatorul de dezvoltare, înainte de IIS. Backend-ul rulează în mediul
**Development**: login simulat (fără IIS / Windows Authentication), cu AD real, iar Exchange și emailul sunt implicit
simulate.

## 1. Ce trebuie instalat

| Ce | Observații |
|---|---|
| Visual Studio 2026 (sau 2022 actualizat) cu workload-ul **ASP.NET and web development** | include **SQL Server Express LocalDB** |
| .NET 10 SDK | vine cu Visual Studio sau din `offline/` (vezi [offline/README.md](../offline/README.md)) |
| Node.js 22 | pentru frontend |
| Acces la un controler de domeniu | pentru AD real: portul 636 (LDAPS) sau 389 de pe calculatorul dvs. |

Pachetele NuGet și npm sunt în repository (`offline/`), deci restore-ul merge și fără internet.

## 2. Configurare: `backend/src/MDel.Api/appsettings.Development.json`

Înlocuiți conținutul cu (adaptați domeniul, serverul, contul și OU-urile):

```json
{
  "ConnectionStrings": {
    "MDel": "Server=(localdb)\\MSSQLLocalDB;Database=MDel_Dev;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Authentication": {
    "Mode": "Development",
    "Development": {
      "User": "CORP\\ion.popescu",
      "Groups": [ "CORP\\MDel-Admins", "CORP\\MDel-Approvers" ]
    }
  },
  "ActiveDirectory": {
    "Enabled": true,
    "Server": "dc01.corp.local",
    "Port": 636,
    "UseSsl": true,
    "ValidateCertificate": true,
    "BindUser": "CN=svc-mdel,OU=Servicii,DC=corp,DC=local",
    "BindPassword": "parola-contului",
    "SearchBases": [ "OU=Angajati,DC=corp,DC=local" ]
  },
  "Setup": {
    "InitialAdmins": [ "CORP\\ion.popescu" ],
    "OverwriteSettingsOnStartup": true,
    "InitialSettings": {
      "Groups.Admins": "CORP\\MDel-Admins",
      "Groups.Approvers": "CORP\\MDel-Approvers",
      "Attachments.StoragePath": "C:\\MDel\\Files",
      "General.BaseUrl": "http://localhost:5173"
    }
  },
  "Integrations": {
    "UseFakes": true
  },
  "DataProtection": {
    "KeysPath": ""
  }
}
```

| Cheie | Ce face local |
|---|---|
| `ConnectionStrings:MDel` | LocalDB: baza `MDel_Dev` se creează singură la prima pornire. Alt SQL Server: `Server=NUME\\SQLEXPRESS;Database=MDel_Dev;Trusted_Connection=True;TrustServerCertificate=True`. Fără SQL deloc: adăugați `"Database": { "UseInMemory": true }` (datele se pierd la oprire). |
| `Authentication:Development:User` | utilizatorul cu care „sunteți logat”: puneți **contul dvs. din AD** (numele, emailul se iau din AD). |
| `Authentication:Development:Groups` | grupurile simulate ale acestui utilizator (dau rolurile Admin / Aprobator). |
| `ActiveDirectory` | conexiunea la **AD-ul real** (detalii: [AD-SETUP.md](AD-SETUP.md)). Cu `Enabled: true` se folosește AD real chiar dacă `UseFakes` e `true`. |
| `Setup:InitialSettings` | completează Setările la pornire (grupuri, folderul pentru fișiere, adresa pentru linkurile din emailuri). |
| `Integrations:UseFakes` | `true` = Exchange și emailul sunt **simulate**: forward-ul nu se aplică pe căsuțe, emailurile apar în Setări → Notificări email. Separat: `"FakeExchange": false` = Exchange real (din Setări → Exchange), `"FakeMail": false` = SMTP real. |

Fișierul conține o parolă: nu îl puneți în git cu date reale. Alternativ, valorile sensibile se pot ține în
*User Secrets* (în Visual Studio: click dreapta pe `MDel.Api` → **Manage User Secrets**, aceeași structură JSON).

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

1. **Setări → Active Directory → Testează conexiunea**: trebuie să arate serverul, contul și fiecare OU cu „OK”.
   La eroare, mesajul spune cauza (parolă, port, certificat, OU).
2. **Delegare nouă**: căutați sursa și destinatarul (vin din AD-ul real), date de început/sfârșit, fișier atașat.
3. **Aprobare cu alt utilizator**: deschideți în browser (înlocuiți cu un cont real din AD):

   ```
   http://localhost:5173/api/dev/login-as?user=dan.georgescu&groups=MDel-Approvers
   ```

   Sunteți acum `dan.georgescu`, cu rol de aprobator; mergeți la **Aprobări**. Alte exemple:
   - `?user=maria.ionescu&groups=none` – utilizator fără rol (trebuie să vadă „Nu aveți drepturi”);
   - `?user=ion.popescu&groups=MDel-Admins,MDel-Approvers` – admin și aprobator;
   - fără parametri (`/api/dev/login-as`) – înapoi la utilizatorul din `appsettings.Development.json`.
4. **Activare / expirare**: jobul automat rulează la 30 de secunde; o delegare cu început „acum” devine activă în
   cel mult 30 s, iar la ora de sfârșit expiră. Cu Exchange simulat, forward-ul nu ajunge pe căsuțe.
5. **Emailuri**: Setări → Notificări email arată ce s-ar fi trimis (destinatar, subiect, stare).
6. **Audit**: Istoric / Audit arată toate acțiunile.

Checklist-ul complet: [UAT.md](UAT.md) (pașii cu Exchange real se fac pe mediul de test, cu `FakeExchange: false`
sau pe IIS).

## 6. Login real cu Windows (opțional)

Login-ul simulat e doar pentru dezvoltare. Autentificarea Windows reală (Kerberos/NTLM) se verifică pe IIS
(vezi [DEPLOY.md](DEPLOY.md)); prin serverul de dezvoltare al frontend-ului (portul 5173) NTLM nu funcționează.

## Probleme frecvente

| Simptom | Cauză probabilă |
|---|---|
| `npm run dev` pornește, dar pagina arată „Nu se poate încărca” / erori 502 | backend-ul nu rulează pe 5080 (profilul **http** din Visual Studio) |
| Căutarea de utilizatori găsește doar „Dev Admin”, „Ion Popescu” | AD simulat: `ActiveDirectory:Enabled` nu e `true` în `appsettings.Development.json` |
| Testul AD: „cannot reach … over SSL” | portul 636 blocat de pe calculatorul dvs. sau certificatul DC-ului nu e de încredere; încercați temporar `"ValidateCertificate": false` |
| Eroare la pornire despre SQL / LocalDB | LocalDB lipsește: Visual Studio Installer → Individual components → *SQL Server Express LocalDB*, sau folosiți `Database:UseInMemory` |
| Încărcarea fișierelor eșuează | `Attachments.StoragePath` lipsă sau folderul nu poate fi creat (Setări → Fișiere → Testează) |
