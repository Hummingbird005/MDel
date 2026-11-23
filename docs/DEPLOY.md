# MDel – ghid de instalare (IIS, Windows Server)

Ghid pas cu pas pentru instalarea MDel pe un server IIS din domeniu, cu SQL Server și Exchange 2016 on-premise.
Bifați fiecare pas; la final rulați checklist-ul din [UAT.md](UAT.md).

## 1. Ce vă trebuie

| Componentă | Detalii |
|---|---|
| Server aplicație | Windows Server 2019/2022, membru al domeniului, IIS cu **Windows Authentication** |
| Runtime | **ASP.NET Core 10 Hosting Bundle** (include modulul IIS) |
| Bază de date | SQL Server 2017+; o bază goală, de ex. `MDel` |
| Exchange | Exchange 2016, endpoint PowerShell `http://<server>/PowerShell/` accesibil din serverul aplicației (WinRM, port 80) |
| SMTP | Receive connector Exchange pentru relay intern (port 25) sau client submission (587) |
| Cont de serviciu | De ex. `CONTOSO\svc-mdel`: identitatea app pool-ului (recomandat) sau cont introdus în Setări |
| Grupuri AD | Administratori (ex. `CONTOSO\MDel-Admins`) și aprobatori (ex. `CONTOSO\MDel-Approvers`), cu adrese de email |
| Folder fișiere | Disc local sau share UNC, de ex. `D:\MDel\Files` |

## 2. Pachetul de instalare

Pachetul se construiește cu `scripts/Publish-MDel.ps1` (Windows) sau `scripts/publish.sh` (Linux/CI). CI-ul îl
produce automat la fiecare merge în `main`, ca artefact **mdel-release** (GitHub → Actions → CI → Artifacts).

```
release/
  MDel/                  aplicația (API + interfața în wwwroot) – se copiază în folderul site-ului
  efbundle.exe           aplică migrările bazei de date
  mdel-migrations.sql    același lucru ca script SQL idempotent (pentru DBA)
  scripts/               rol RBAC Exchange + smoke-test
  docs/                  acest ghid și checklist-ul UAT
```

## 3. Baza de date

1. Creați baza goală și dați contului app pool-ului drepturi `db_datareader`, `db_datawriter`
   (și `db_ddladmin` doar dacă migrările se aplică cu acest cont).
2. Aplicați migrările – **una** dintre variante:
   - `.\efbundle.exe --connection "Server=SQL01;Database=MDel;Trusted_Connection=True;TrustServerCertificate=True"`
     (rulat de un cont cu drept de creare tabele);
   - sau DBA-ul rulează `mdel-migrations.sql` (idempotent – poate fi rulat de mai multe ori).
3. Verificare: există schema `mdel` cu tabelele `AppUsers`, `Delegations`, `AuditLogs` etc. și tabela
   `mdel.__EFMigrationsHistory` cu migrările aplicate.

La fiecare versiune nouă: rulați din nou `efbundle.exe` (aplică doar migrările noi), apoi copiați aplicația.

## 4. Exchange

1. În Exchange Management Shell: `scripts\New-MDelRbacRole.ps1 -ServiceAccount CONTOSO\svc-mdel`
   (rol minim: doar `Get-Recipient`, `Get-Mailbox`, `Set-Mailbox -ForwardingAddress/-DeliverToMailboxAndForward`).
2. Testați înainte de aplicație: `scripts\Test-MDelExchange.ps1` pe două căsuțe de test (vezi `scripts/README.md`).

## 5. IIS

1. Copiați `release\MDel` în `C:\inetpub\MDel`.
2. **Application pool** `MDel`:
   - .NET CLR version: **No Managed Code**
   - Identity: `CONTOSO\svc-mdel` (sau ApplicationPoolIdentity, dacă accesul la SQL/Exchange se face cu contul calculatorului)
   - **Start Mode: AlwaysRunning** și **Idle Time-out: 0** – obligatoriu: activările și expirările automate rulează în aplicație
   - Load User Profile: True
3. **Site** (sau aplicație sub un site existent) cu folderul de mai sus, binding **HTTPS** cu certificat intern.
   Opțional pe site: **Preload Enabled = True**.
4. Autentificare: **Windows Authentication = Enabled** (providers: Negotiate, NTLM), Anonymous = Enabled
   (doar pentru `/api/health` și fișierele interfeței; API-ul cere oricum autentificare). `web.config` din pachet le setează deja.
5. Limita de upload (110 MB) e în `web.config` (`maxAllowedContentLength`). Dacă ridicați limita din Setări peste
   105 MB, măriți și această valoare, și `Uploads:MaxRequestBodyBytes`.
6. Doar **o singură instanță** (fără web garden / mai multe servere în balansare): joburile automate nu sunt coordonate între instanțe.

## 6. Configurare (`appsettings.json` în folderul aplicației)

```json
{
  "ConnectionStrings": { "MDel": "Server=SQL01;Database=MDel;Trusted_Connection=True;TrustServerCertificate=True" },
  "Authentication": {
    "Mode": "Negotiate",
    "Groups": { "Admins": "CONTOSO\\MDel-Admins", "Approvers": "CONTOSO\\MDel-Approvers" }
  },
  "Integrations": { "UseFakes": false },
  "DataProtection": { "KeysPath": "C:\\ProgramData\\MDel\\keys" },
  "Scheduler": { "Enabled": true, "IntervalSeconds": 30, "ReconcileMinutes": 60 }
}
```

- `Authentication:Groups` – folosite doar până la completarea grupurilor în Setări (primul acces al adminului).
- `DataProtection:KeysPath` – folderul cheilor care criptează parolele din Setări. Contul app pool-ului trebuie să aibă
  drept de **scriere**. Includeți-l în backup; dacă se pierde, parolele din Setări trebuie reintroduse.
- Folderul de fișiere (Setări → Fișiere) – drept de **modificare** pentru contul app pool-ului.

## 7. Prima pornire

1. Deschideți `https://mdel.contoso.local/api/health/ready` – trebuie să răspundă `"status":"ready"`
   (bază accesibilă, fără migrări neaplicate). `503` = verificați connection string-ul / migrările.
2. Deschideți aplicația cu un cont din grupul de administratori → **Setări** și completați, testând fiecare tab:
   1. **Active Directory** – domeniu, server LDAP, Base DN (+ cont, dacă nu se folosește identitatea app pool-ului) → Testează conexiunea.
   2. **Grupuri** – grupurile AD și adresele lor de email.
   3. **Exchange** – `http://ex01.contoso.local/PowerShell/` (+ cont) → Testează; verificați forwardul unei căsuțe de test.
   4. **Email (SMTP)** – server, port, expeditor → Testează, cu email de test.
   5. **Fișiere** – calea de stocare → Testează (scriere/citire/ștergere).
   6. **General** – adresa aplicației (pentru link-urile din emailuri), limba implicită.
   7. **Grup de informare** – utilizatorii care primesc toate notificările.
   8. **Șabloane email** (opțional) – adaptați textul mesajelor în română; verificați în previzualizare.
3. Rulați checklist-ul din [UAT.md](UAT.md).

## 8. Operare

| Ce | Unde |
|---|---|
| Stare aplicație | `/api/health` (proces), `/api/health/ready` (bază de date + migrări) |
| Emailuri netrimise | Setări → Notificări email (număr afișat lângă „Setări”), cu Retrimite |
| Erori Exchange | Delegările în starea **Eroare** (Dashboard → „Oprite / respinse / erori”); se reîncearcă automat la 10 minute |
| Modificări manuale în Exchange | reconcilierea orară le marchează pe delegare și anunță administratorii |
| Cine a făcut ce | Istoric / Audit (filtre, detaliu cu valori vechi/noi, export CSV) |
| Loguri tehnice | stdout-ul ASP.NET Core: în `web.config`, `stdoutLogEnabled="true"` temporar, sau Event Viewer |

**Backup:** baza de date, folderul de fișiere și folderul cheilor Data Protection.

**Actualizare:** opriți app pool-ul → `efbundle.exe --connection "..."` → copiați noul `MDel` peste cel vechi
(păstrați `appsettings.json`) → porniți app pool-ul → verificați `/api/health/ready`.

## 9. Probleme frecvente

| Simptom | Cauză probabilă |
|---|---|
| „Autentificarea AD a eșuat” în browser | Windows Authentication dezactivat în IIS, sau site-ul nu e în zona Intranet a browserului (Kerberos/NTLM automat) |
| Utilizatorul intră, dar fără meniuri | nu e în grupurile configurate (Setări → Grupuri sau `Authentication:Groups`); rolurile se reîmprospătează în max. 5 minute |
| Test Exchange: „Access denied” / „Kerberos” | contul nu are rolul RBAC sau `RemotePowerShellEnabled`; URI-ul trebuie să folosească numele serverului (nu IP) pentru Kerberos |
| Delegările nu se activează la oră | app pool-ul s-a oprit (Start Mode / Idle Time-out); `Scheduler:Enabled` = false |
| „Secret cannot be decrypted” | cheile Data Protection s-au schimbat/pierdut – reintroduceți parolele în Setări |
| Upload eșuat la fișiere mari (413) | `maxAllowedContentLength` din `web.config` / `Uploads:MaxRequestBodyBytes` |
