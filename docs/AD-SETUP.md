# MDel – pregătirea Active Directory înainte de prima pornire

Pașii de mai jos se fac **o singură dată**, înainte de instalarea din [DEPLOY.md](DEPLOY.md). La final aveți:
contul de serviciu, grupurile de administratori și aprobatori, SPN-urile pentru Kerberos, drepturile în SQL Server
și secțiunea `Setup` din `appsettings.json` completată, inclusiv **adminul inițial** cu care vă conectați prima dată.

Exemplele folosesc domeniul `contoso.local` (NetBIOS `CONTOSO`) și adresa aplicației `https://mdel.contoso.local`.

## Pe scurt

| # | Ce | Cine / unde |
|---|---|---|
| 1 | Cont de serviciu `CONTOSO\svc-mdel` | AD (script) |
| 2 | Grupurile `MDel-Admins` și `MDel-Approvers`, cu membri și adrese de email | AD + Exchange (script) |
| 3 | Înregistrare DNS și SPN-uri `HTTP/mdel.contoso.local` | DNS + AD (script) |
| 4 | Site-ul în zona *Local intranet* a browserelor | GPO |
| 5 | Drepturi SQL Server pentru contul de serviciu | SQL Server |
| 6 | Rol RBAC Exchange pentru contul de serviciu | Exchange (`scripts/New-MDelRbacRole.ps1`) |
| 7 | `appsettings.json`: admin inițial + setări AD | serverul aplicației |

Pașii 1–3 se pot face automat cu `scripts/New-MDelAdObjects.ps1` (vezi mai jos) sau manual.

## 1–3. Contul de serviciu, grupurile, SPN-urile – automat

Pe o stație din domeniu cu RSAT (modulul ActiveDirectory), cu un cont care poate crea obiecte în OU-ul ales.
Rulat din **Exchange Management Shell**, cu `-MailEnableGroups`, grupurile primesc și adrese de email.

```powershell
# 1) Ce s-ar face, fără modificări:
.\scripts\New-MDelAdObjects.ps1 -OrganizationalUnit 'OU=MDel,OU=Servicii,DC=contoso,DC=local' `
    -AppHostName mdel.contoso.local -AdminMembers ion.popescu,maria.ionescu -ApproverMembers dan.georgescu `
    -MailEnableGroups -WhatIf

# 2) Aplicare (cere parola contului de serviciu, dacă îl creează):
.\scripts\New-MDelAdObjects.ps1 -OrganizationalUnit 'OU=MDel,OU=Servicii,DC=contoso,DC=local' `
    -AppHostName mdel.contoso.local -AdminMembers ion.popescu,maria.ionescu -ApproverMembers dan.georgescu `
    -MailEnableGroups
```

Scriptul poate fi rulat de mai multe ori: completează doar ce lipsește. La final afișează secțiunea `Setup` gata
completată pentru `appsettings.json` (pasul 7).

## 1–3. Manual

**1. Cont de serviciu** – utilizator obișnuit, de ex. `svc-mdel`: parola nu expiră, fără drepturi de administrator.
Va fi identitatea app pool-ului IIS și accesează cu acest cont SQL Server, AD (citire LDAP) și Exchange.
Citirea din AD nu cere drepturi speciale: orice utilizator autentificat poate căuta conturi.

**2. Grupuri** – grupuri de **securitate**, cu scope **Universal** (necesar pentru adresă de email în Exchange):

| Grup | Rol în aplicație | Membri |
|---|---|---|
| `CONTOSO\MDel-Admins` | administrator: delegări, oprire, Setări, Audit | administratorii de sistem |
| `CONTOSO\MDel-Approvers` | aprobator: aprobă / respinge cererile | aprobatorii (alții decât cei care creează cererile) |

Grupul de informare (cei care primesc doar notificări) **nu** e grup AD: se completează din aplicație, Setări → Grup de informare.

Adrese de email (notificările către administratori / aprobatori se trimit pe adresa grupului):

```powershell
# Exchange Management Shell
Enable-DistributionGroup -Identity MDel-Admins
Enable-DistributionGroup -Identity MDel-Approvers
Get-DistributionGroup MDel-* | Format-Table Name, PrimarySmtpAddress
```

**3. DNS și SPN-uri (Kerberos)**

- Înregistrare DNS de tip **A** (nu CNAME) `mdel.contoso.local` → IP-ul serverului IIS.
- Dacă app pool-ul rulează cu contul de serviciu și site-ul se deschide cu acest nume, SPN-urile HTTP trebuie să
  fie pe contul de serviciu (altfel browserul cade pe NTLM sau autentificarea eșuează):

```cmd
setspn -S HTTP/mdel.contoso.local CONTOSO\svc-mdel
setspn -S HTTP/mdel CONTOSO\svc-mdel
setspn -L CONTOSO\svc-mdel
```

`-S` refuză duplicatele. Un SPN `HTTP/...` pus pe două conturi strică Kerberos.

- În IIS, la site: **Configuration Editor** → `system.webServer/security/authentication/windowsAuthentication`
  → `useAppPoolCredentials = True` (cu Kernel-mode authentication activ, valoarea implicită), sau:

```cmd
%windir%\system32\inetsrv\appcmd.exe set config "MDel" -section:system.webServer/security/authentication/windowsAuthentication /useAppPoolCredentials:"True" /commit:apphost
```

Dacă app pool-ul rulează ca *ApplicationPoolIdentity* (cont de calculator), nu adăugați SPN-uri: cele implicite
`HOST/<server>` acoperă numele serverului; pentru un nume DNS propriu, SPN-ul `HTTP/mdel.contoso.local` se pune pe
contul de calculator al serverului IIS.

## 4. Browserele: zona Local intranet

Ca utilizatorii să intre fără să li se ceară parola, adresa aplicației trebuie să fie în zona *Local intranet*.
GPO → User Configuration → Administrative Templates → Windows Components → Internet Explorer → Internet Control Panel
→ Security Page → **Site to Zone Assignment List**: `https://mdel.contoso.local` = `1`.
Edge și Chrome folosesc aceleași zone.

## 5. SQL Server

Baza de date **se creează singură la prima pornire a aplicației** (cu toate tabelele) și se actualizează la fiecare
versiune nouă (`Setup:MigrateDatabaseOnStartup = true`). Contul de serviciu are nevoie de una dintre variante:

```sql
-- A) Aplicația creează baza (cel mai simplu):
CREATE LOGIN [CONTOSO\svc-mdel] FROM WINDOWS;
ALTER SERVER ROLE dbcreator ADD MEMBER [CONTOSO\svc-mdel];   -- creatorul bazei devine owner (db_owner)

-- B) DBA-ul creează baza goală, aplicația creează tabelele:
CREATE DATABASE MDel;
CREATE LOGIN [CONTOSO\svc-mdel] FROM WINDOWS;
USE MDel;
CREATE USER [CONTOSO\svc-mdel] FOR LOGIN [CONTOSO\svc-mdel];
ALTER ROLE db_owner ADD MEMBER [CONTOSO\svc-mdel];
```

După prima pornire, la varianta A, rolul `dbcreator` poate fi retras
(`ALTER SERVER ROLE dbcreator DROP MEMBER [CONTOSO\svc-mdel];`), pentru că baza există și contul rămâne owner.
Dacă DBA-ul preferă să aplice el modificările de structură, setați `MigrateDatabaseOnStartup` = `false` și folosiți
`efbundle.exe` / `mdel-migrations.sql` din pachet (vezi [DEPLOY.md](DEPLOY.md), secțiunea 3).

## 6. Exchange

```powershell
# Exchange Management Shell, cont din Organization Management
.\scripts\New-MDelRbacRole.ps1 -ServiceAccount CONTOSO\svc-mdel
```

Detalii și testare: [scripts/README.md](../scripts/README.md).

## 7. `appsettings.json`: adminul inițial și setările AD

În folderul aplicației (ex. `C:\inetpub\MDel\appsettings.json`), secțiunea `Setup`:

```json
"Setup": {
  "MigrateDatabaseOnStartup": true,
  "InitialAdmins": [ "CONTOSO\\ion.popescu" ],
  "InitialSettings": {
    "Ad.Domain": "contoso.local",
    "Ad.LdapServer": "",
    "Ad.BaseDn": "DC=contoso,DC=local",
    "Groups.Admins": "CONTOSO\\MDel-Admins",
    "Groups.AdminsEmail": "mdel-admins@contoso.local",
    "Groups.Approvers": "CONTOSO\\MDel-Approvers",
    "Groups.ApproversEmail": "mdel-approvers@contoso.local",
    "General.BaseUrl": "https://mdel.contoso.local"
  }
}
```

În JSON, backslash-ul se scrie dublat: `CONTOSO\\ion.popescu`.

| Cheie | Ce face |
|---|---|
| `InitialAdmins` | **Adminul inițial**: conturile din listă sunt administratori chiar dacă nu sunt (încă) în grupul `MDel-Admins`. Format `CONTOSO\\cont` sau doar `cont`. Conectarea se face tot cu Windows, cu parola contului din AD. Aplicația nu are parole proprii. |
| `InitialSettings` | Valori copiate în **Setări** la prima pornire, doar pentru câmpurile încă goale. Ce salvați ulterior din ecranul Setări nu se suprascrie, deci secțiunea poate rămâne în fișier. |
| `Ad.LdapServer` | gol = controlerul de domeniu se găsește prin DNS din `Ad.Domain`; altfel `dc01.contoso.local` sau `ldaps://dc01.contoso.local:636` |
| `Ad.BaseDn` | gol = derivat din domeniu (`DC=contoso,DC=local`); se poate restrânge la un OU |
| `Groups.*` | grupurile și adresele de la pasul 2 |
| `General.BaseUrl` | adresa aplicației, pentru linkurile din emailuri |

Se pot pune și alte setări (cheile din Setări, ex. `Exchange.PowerShellUri`, `Smtp.Host`, `Attachments.StoragePath`).
Parolele **nu** se pun aici: se introduc din ecranul Setări, unde sunt criptate.

## Prima pornire și verificare

1. Porniți site-ul (app pool-ul). Cu `stdoutLogEnabled="true"` în `web.config` (temporar), în `logs\stdout_*.log`
   apar: `Applying 4 database migration(s)…`, `Database migrations applied.`, `Initial settings applied: Ad.Domain, …`.
   Erorile de pornire apar oricum în Event Viewer → Windows Logs → Application.
2. `https://mdel.contoso.local/api/health/ready` → `"status":"ready"`.
3. Deschideți aplicația cu contul din `InitialAdmins`. Vedeți meniurile Dashboard, Delegări, Istoric/Audit și Setări.
4. **Setări → Active Directory → Testează conexiunea**, apoi celelalte taburi (Exchange, SMTP, Fișiere), ca în
   [DEPLOY.md](DEPLOY.md), secțiunea 7.
5. Verificați că un membru al grupului `MDel-Admins` intră ca administrator, apoi **scoateți-vă din `InitialAdmins`**
   (lista goală `[]`) și reporniți app pool-ul. De acum rolurile vin doar din grupuri.

## Probleme frecvente

| Simptom | Cauză probabilă |
|---|---|
| Se cere parola în browser / „401” | site-ul nu e în zona Local intranet; SPN lipsă sau duplicat (`setspn -X`); `useAppPoolCredentials` neactivat |
| App pool-ul se oprește la pornire (HTTP 500.30) | baza nu poate fi creată: connection string sau drepturi SQL (pasul 5); detaliile apar în Event Viewer |
| `Setup:InitialSettings contains invalid values` | o valoare invalidă (ex. email greșit): corectați-o în `appsettings.json` |
| Adminul inițial intră, dar fără nume/email | AD nu e încă accesibil sau configurat: se completează la prima conectare după ce testul AD trece |
| Un membru nou al grupului nu are drepturi | trebuie să se deconecteze/reconecteze în Windows (grupurile sunt în tichetul Kerberos); în aplicație rolurile se reîmprospătează în max. 5 minute |
