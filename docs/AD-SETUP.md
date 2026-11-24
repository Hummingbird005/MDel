# MDel – pregătirea Active Directory înainte de prima pornire

Pașii de mai jos se fac **o singură dată**, înainte de instalarea din [DEPLOY.md](DEPLOY.md). La final aveți contul de
serviciu, cele trei grupuri AD (câte unul pe rol), SPN-urile pentru Kerberos și drepturile în SQL Server.
**Configurarea AD în aplicație se face strict din interfață** (Setări → Active Directory), nu din fișiere:
în `appsettings.json` nu se pune niciun cont sau parolă.

Exemplele folosesc domeniul `corp.local` (NetBIOS `CORP`) și adresa aplicației `https://mdel.corp.local`.

## Rolurile și grupurile

Un utilizator poate avea oricâte roluri, câte unul din fiecare grup AD în care e membru.

| Rol | Grup AD (exemplu) | Ce poate face |
|---|---|---|
| **Administrator delegări** (`Admin`) | `CORP\MDel-Admins` | creează, modifică și oprește delegări; vede dashboard-ul și toate delegările |
| **Aprobator** (`Approver`) | `CORP\MDel-Approvers` | aprobă / respinge cererile |
| **Administrator sistem** (`SystemAdmin`) | `CORP\MDel-System` | meniurile **Setări** și **Istoric / Audit** (AD, Exchange, SMTP, grupuri, șabloane, coada de emailuri, audit) |

Grupurile se completează în aplicație, la **Setări → Grupuri** (sau, doar pentru prima pornire, în `appsettings.json`,
vezi mai jos). Un administrator de delegări **nu** vede Setări și Audit decât dacă e și în grupul de administratori de
sistem. Grupul de informare (cei care primesc doar notificări) nu e grup AD: se completează din Setări → Grup de informare.

## Pe scurt

| # | Ce | Cine / unde |
|---|---|---|
| 1 | Cont de serviciu `CORP\svc-mdel` | AD |
| 2 | Grupurile `MDel-Admins`, `MDel-Approvers`, `MDel-System`, cu membri | AD (+ Exchange pentru adrese de email) |
| 3 | Înregistrare DNS și SPN-uri `HTTP/mdel.corp.local` | DNS + AD |
| 4 | Site-ul în zona *Local intranet* a browserelor | GPO |
| 5 | Drepturi SQL Server pentru contul de serviciu | SQL Server |
| 6 | Rol RBAC Exchange pentru contul de serviciu | Exchange (`scripts/New-MDelRbacRole.ps1`) |
| 7 | `appsettings.json`: connection string și **adminul inițial** | serverul aplicației |
| 8 | Prima pornire → **Setări → Active Directory** (server, SSL, OU-uri, cont) → **Grupuri** | aplicația |

## 1–3. Contul de serviciu, grupurile, SPN-urile

**Cu PowerShell** (opțional) – `scripts/New-MDelAdObjects.ps1` creează tot ce lipsește, idempotent:

```powershell
.\scripts\New-MDelAdObjects.ps1 -OrganizationalUnit 'OU=MDel,OU=Servicii,DC=corp,DC=local' `
    -AppHostName mdel.corp.local -AdminMembers ion.popescu -ApproverMembers dan.georgescu `
    -SystemAdminMembers ion.popescu -MailEnableGroups -WhatIf     # fără -WhatIf aplică modificările
```

**Fără PowerShell** – cereți echipei AD sau folosiți *Active Directory Users and Computers*:

1. **Cont de serviciu** – utilizator obișnuit, de ex. `svc-mdel`: parola nu expiră, fără drepturi de administrator.
   Va fi identitatea app pool-ului IIS (accesează SQL Server și Exchange). Pentru citirea din AD se poate folosi același
   cont sau altul; **orice cont de domeniu obișnuit poate căuta utilizatori**, nu cere drepturi speciale.
2. **Trei grupuri** de securitate, scope **Universal** (necesar pentru adresă de email în Exchange):
   `MDel-Admins`, `MDel-Approvers`, `MDel-System`. Adăugați membrii.
3. Adrese de email pentru grupurile de administratori și aprobatori (notificările se trimit pe ele):

```powershell
# Exchange Management Shell
Enable-DistributionGroup -Identity MDel-Admins
Enable-DistributionGroup -Identity MDel-Approvers
Get-DistributionGroup MDel-* | Format-Table Name, PrimarySmtpAddress
```

**3. DNS și SPN-uri (Kerberos)**

- Înregistrare DNS de tip **A** (nu CNAME) `mdel.corp.local` → IP-ul serverului IIS.
- Dacă app pool-ul rulează cu contul de serviciu și site-ul se deschide cu acest nume, SPN-urile HTTP trebuie să
  fie pe contul de serviciu (altfel browserul cade pe NTLM sau autentificarea eșuează):

```cmd
setspn -S HTTP/mdel.corp.local CORP\svc-mdel
setspn -S HTTP/mdel CORP\svc-mdel
setspn -L CORP\svc-mdel
```

`-S` refuză duplicatele. Un SPN `HTTP/...` pus pe două conturi strică Kerberos.

- În IIS, la site: **Configuration Editor** → `system.webServer/security/authentication/windowsAuthentication`
  → `useAppPoolCredentials = True` (cu Kernel-mode authentication activ, valoarea implicită), sau:

```cmd
%windir%\system32\inetsrv\appcmd.exe set config "MDel" -section:system.webServer/security/authentication/windowsAuthentication /useAppPoolCredentials:"True" /commit:apphost
```

Dacă app pool-ul rulează ca *ApplicationPoolIdentity* (cont de calculator), nu adăugați SPN-uri: cele implicite
`HOST/<server>` acoperă numele serverului; pentru un nume DNS propriu, SPN-ul `HTTP/mdel.corp.local` se pune pe
contul de calculator al serverului IIS.

## 4. Browserele: zona Local intranet

Ca utilizatorii să intre fără să li se ceară parola, adresa aplicației trebuie să fie în zona *Local intranet*.
GPO → User Configuration → Administrative Templates → Windows Components → Internet Explorer → Internet Control Panel
→ Security Page → **Site to Zone Assignment List**: `https://mdel.corp.local` = `1`.
Edge și Chrome folosesc aceleași zone.

## 5. SQL Server

Baza de date **se creează singură la prima pornire a aplicației** (cu toate tabelele) și se actualizează la fiecare
versiune nouă (`Setup:MigrateDatabaseOnStartup = true`). Contul de serviciu are nevoie de una dintre variante:

```sql
-- A) Aplicația creează baza (cel mai simplu):
CREATE LOGIN [CORP\svc-mdel] FROM WINDOWS;
ALTER SERVER ROLE dbcreator ADD MEMBER [CORP\svc-mdel];   -- creatorul bazei devine owner (db_owner)

-- B) DBA-ul creează baza goală, aplicația creează tabelele:
CREATE DATABASE MDel;
CREATE LOGIN [CORP\svc-mdel] FROM WINDOWS;
USE MDel;
CREATE USER [CORP\svc-mdel] FOR LOGIN [CORP\svc-mdel];
ALTER ROLE db_owner ADD MEMBER [CORP\svc-mdel];
```

După prima pornire, la varianta A, rolul `dbcreator` poate fi retras
(`ALTER SERVER ROLE dbcreator DROP MEMBER [CORP\svc-mdel];`), pentru că baza există și contul rămâne owner.
Dacă DBA-ul preferă să aplice el modificările de structură, setați `MigrateDatabaseOnStartup` = `false` și folosiți
`efbundle.exe` / `mdel-migrations.sql` din pachet (vezi [DEPLOY.md](DEPLOY.md), secțiunea 3).

## 6. Exchange

```powershell
# Exchange Management Shell, cont din Organization Management
.\scripts\New-MDelRbacRole.ps1 -ServiceAccount CORP\svc-mdel
```

Detalii și testare: [scripts/README.md](../scripts/README.md).

## 7. `appsettings.json`: connection string și adminul inițial

În folderul aplicației (ex. `C:\inetpub\MDel\appsettings.json`). **Active Directory nu se configurează aici**, iar
conturile și parolele nu se pun niciodată în fișier.

```json
{
  "ConnectionStrings": { "MDel": "Server=SQL01;Database=MDel;Trusted_Connection=True;TrustServerCertificate=True" },
  "Setup": {
    "MigrateDatabaseOnStartup": true,
    "InitialAdmins": [ "CORP\\ion.popescu" ]
  }
}
```

În JSON, backslash-ul se scrie dublat (`CORP\\ion.popescu`).

### Adminul inițial (`Setup:InitialAdmins`)

Conturile din listă primesc **toate cele trei roluri**, fiecare **până când se completează grupul acelui rol**:

| Situație | Rezultat pentru adminul inițial |
|---|---|
| Niciun grup completat (prima pornire) | are `Admin`, `Approver` și `SystemAdmin` – poate configura totul |
| Ați completat **Grup administratori de sistem** | pierde `SystemAdmin` (rolul vine doar din grup), le păstrează pe celelalte |
| Ați completat toate cele trei grupuri | nu mai are niciun rol din `InitialAdmins`; rolurile vin doar din grupuri |

Grupul „completat” = valoarea din Setări → Grupuri sau, dacă aceea e goală, `Authentication:Groups:Admins` /
`Approvers` / `SystemAdmins` din `appsettings.json`. **Înainte să completați Grup administratori de sistem, asigurați-vă
că sunteți membru al lui** (altfel vă pierdeți accesul la Setări). Rolurile se reîmprospătează în max. 5 minute.

Conectarea se face cu contul Windows (parola din AD); aplicația nu are parole proprii.

### Setări non-secrete la prima pornire (opțional)

`Setup:InitialSettings` copiază în Setări valori **nesecrete** (grupuri, adresa aplicației, domeniul AD…), doar dacă sunt
goale, sau la fiecare pornire cu `Setup:OverwriteSettingsOnStartup = true`:

```json
"Setup": {
  "InitialSettings": {
    "Groups.SystemAdmins": "CORP\\MDel-System",
    "Groups.Admins": "CORP\\MDel-Admins",
    "Groups.Approvers": "CORP\\MDel-Approvers",
    "General.BaseUrl": "https://mdel.corp.local"
  }
}
```

Conturile și parolele (`Ad.ServiceUser`, `Ad.ServicePassword`, `Exchange.*`, `Smtp.*`) sunt **ignorate** aici (cu avertisment
în log): se introduc doar din interfață.

## 8. Prima pornire: configurarea AD din interfață

1. Porniți app pool-ul: baza se creează singură. Deschideți `https://mdel.corp.local/api/health/ready` →
   `"status":"ready"`.
2. Deschideți aplicația cu contul din `InitialAdmins` → **Setări** → **Active Directory**:

| Câmp | Ce se completează |
|---|---|
| Domeniu AD | `corp.local` |
| Server LDAP / controler de domeniu | `dc01.corp.local` (sau `ldaps://dc01.corp.local:636`); gol = controlerul se găsește după domeniu |
| Port | gol = 636 cu SSL, 389 fără |
| Folosește SSL (LDAPS) | **da**, recomandat (port 636); obligatoriu dacă contul e dat ca DN |
| Verifică certificatul serverului | da; dezactivați doar pentru teste cu certificat self-signed |
| OU-uri de căutare | câte un OU pe linie, ex. `OU=Angajati,DC=corp,DC=local`; gol = tot domeniul. Conturile dezactivate nu apar |
| Cont de citire din AD | `CORP\svc-mdel`, `svc-mdel@corp.local` sau DN (`CN=svc-mdel,OU=Servicii,DC=corp,DC=local`); gol = identitatea app pool-ului |
| Parola contului | parola contului de mai sus |

3. **Salvați**, apoi **Testează conexiunea**: arată serverul, portul, contul și fiecare OU („OK” / inexistent). La eroare,
   mesajul spune cauza: cont sau parolă greșite, cont blocat / dezactivat / expirat, server sau port inaccesibil,
   certificat neacceptat, SSL necesar.
4. **Setări → Grupuri**: completați grupurile celor trei roluri (și adresele de email ale administratorilor / aprobatorilor).
   Completați **ultimul** grupul de administratori de sistem, după ce v-ați adăugat în el (vezi tabelul de la pasul 7).
5. Verificați că un membru din fiecare grup are rolul așteptat (în colțul de sus al aplicației apar rolurile), apoi
   goliți `InitialAdmins` din `appsettings.json` și reporniți app pool-ul.

### Cum se păstrează conturile și parolele

- Contul și parola de citire din AD, contul și parola Exchange, contul și parola SMTP se salvează **criptate în baza de
  date** (ASP.NET Core Data Protection); nu apar în cod, în `appsettings.json`, în API (interfața arată doar „setat”) sau în audit.
- Cheile de criptare sunt în `DataProtection:KeysPath` (vezi [DEPLOY.md](DEPLOY.md)): includeți folderul în backup.
  Dacă se pierd, conturile și parolele se reintroduc din Setări.
- La actualizarea de la o versiune în care utilizatorul se salva necriptat, valorile existente se criptează automat la
  prima pornire.

## Probleme frecvente

| Simptom | Cauză probabilă |
|---|---|
| Se cere parola în browser / „401” | site-ul nu e în zona Local intranet; SPN lipsă sau duplicat (`setspn -X`); `useAppPoolCredentials` neactivat |
| App pool-ul se oprește la pornire (HTTP 500.30) | baza nu poate fi creată: connection string sau drepturi SQL (pasul 5); detaliile apar în Event Viewer |
| Intru în aplicație, dar nu văd Setări / Audit | nu aveți rolul **Administrator sistem**: nu sunteți în `InitialAdmins` sau grupul acelui rol e deja completat și nu sunteți în el |
| Nu mai am acces la Setări după ce am completat grupul de sistem | contul nu e membru al grupului; adăugați-l în AD (rolul se reîmprospătează în max. 5 minute) sau, ca măsură de urgență, goliți grupul din baza de date (`UPDATE mdel.AppSettings SET Value = '' WHERE [Key] = 'Groups.SystemAdmins'`) |
| Adminul inițial intră, dar fără nume/email | AD nu e încă configurat: se completează după ce testul AD trece |
| Test AD: „wrong service account or password” | cont sau parolă greșite (pentru DN: verificați exact CN-ul și OU-ul) |
| Test AD: „cannot reach … over SSL” | port 636 blocat, certificatul DC-ului nu e de încredere pe serverul aplicației sau serverul introdus nu e numele din certificat |
| Test AD: „… clear text” / „turn on Use SSL” | un cont dat ca DN (`CN=…`) merge doar cu „Folosește SSL”; fără SSL folosiți `CORP\\cont` |
| Test AD: „Search OU … NoSuchObject” | OU-ul din „OU-uri de căutare” nu există sau e scris greșit |
| `Setup:InitialSettings contains invalid values` | o valoare invalidă în `appsettings.json` (ex. email greșit): corectați-o |
| Un membru nou al unui grup nu are rolul | trebuie să se deconecteze/reconecteze în Windows (grupurile sunt în tichetul Kerberos); în aplicație rolurile se reîmprospătează în max. 5 minute |
