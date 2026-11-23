# MDel – Plan de proiect: forward de căsuță email (Exchange 2016)

Versiune: v2 (include clarificările din 2026-11-23)

## 1. Decizii confirmate

| # | Decizie |
|---|---|
| 1 | Delegare = **doar forwarding de email**, fără drepturi pe inbox (fără Full Access / Send As). |
| 2 | Aprobare: **un singur aprobator** din grup este suficient. |
| 3 | Forward **doar intern** (destinatar din organizație / domenii acceptate). |
| 4 | Administratorii pot **opri** o delegare activă sau programată. |
| 5 | Fișiere pe **disc**, într-o cale definită din ecranul de administrare. |
| 6 | Interfață și emailuri în **română și engleză**. |
| 7 | Exchange 2016 + AD de test există, dar sunt accesibile doar utilizatorului; testarea se face de el, pe baza feedbackului. |

## 2. Stack

- **Backend**: ASP.NET Core (.NET 10), Web API, straturi Api / Application / Domain / Infrastructure.
- **Frontend**: React + TypeScript (Vite), React Query, react-i18next (ro/en).
- **DB**: SQL Server, EF Core + migrații.
- **Auth**: AD (Negotiate/Kerberos sau LDAP + cookie). Rolurile vin din grupuri AD.
- **Exchange**: PowerShell remoting (WinRM, endpoint `/powershell`), cont de serviciu cu rol RBAC minim.
- **Joburi**: Hangfire (storage SQL Server) pentru activare/expirare/reconciliere.
- **Email**: SMTP prin Exchange; template-uri ro/en.
- **Hosting**: IIS pe Windows Server în domeniu.

## 3. Implementare Exchange (forward intern, fără drepturi pe inbox)

- Activare: `Set-Mailbox <From> -ForwardingAddress <To> -DeliverToMailboxAndForward $<copie>`.
  `ForwardingAddress` acceptă doar un destinatar intern (obiect din directorul Exchange), ceea ce respectă regula „doar intern" și nu acordă drepturi pe inbox.
- Se salvează **valoarea anterioară** a forwardului sursei; la expirare/oprire se **restaurează** (nu se golește orbește).
- Un singur forward per căsuță: validare strictă **fără suprapuneri** pe aceeași sursă.
- Opțiune în administrare: păstrează sau nu copie în căsuța sursă (`DeliverToMailboxAndForward`).
- Validare la creare: ambii useri există în AD, au căsuță Exchange, destinatarul este intern, From ≠ To, fără bucle (A→B și B→A).
- Toate apelurile au timeout, retry și sunt auditate.

## 4. Model de date

- **Delegation**: Id, FromUser, ToUser, StartUtc, EndUtc, Status, RequiresApproval, KeepCopy, PreviousForwardValue, CreatedBy/At, ApprovedBy/At, RejectReason, StoppedBy/At/Reason, ExchangeAppliedAt, ExchangeRemovedAt, ErrorInfo.
- **DelegationAttachment**: Id, DelegationId, OriginalName, StoredName, Size, Hash, UploadedBy/At.
- **DelegationNotified**: DelegationId, User/Group, Type (aprobator / admin / delegat / destinatar), SentAt.
- **AppSetting**: cheie-valoare (secretele criptate): AD, Exchange, SMTP, cale stocare fișiere, limită dimensiune (implicit 100 MB), extensii permise, grupuri AD mapate, template-uri, limbă implicită.
- **UserPreference**: limba (ro/en) per utilizator.
- **AuditLog** (append-only): TimestampUtc, User, Action, EntityType, EntityId, Old/New (JSON), IP, Result.
- **Statusuri**: `PendingApproval → Scheduled | Active → Expired`; plus `Rejected`, `Stopped`, `Failed`.

## 5. Flux

1. Adminul creează delegarea (De la, Către, început, sfârșit, atașamente, bifă „fără aprobare").
2. Cu aprobare: `PendingApproval`, mail către grupul aprobatorilor; primul care aprobă o trece în `Active` (dacă startul a trecut) sau `Scheduled`; respingerea → `Rejected` + notificare admin. Aprobarea/respingerea de către al doilea aprobator este blocată (concurență).
3. Fără aprobare: intră direct în `Active`/`Scheduled`; grupul aprobatorilor primește doar notificare.
4. Job la `StartUtc`: aplică forwardul; la eșec → `Failed`, retry, alertă admini.
5. Job la `EndUtc`: elimină forwardul, status `Expired`, notificare către grupurile de admini și utilizatorii implicați.
6. Oprire de către admin: elimină forwardul (dacă e activ) sau anulează programarea, status `Stopped`, cu motiv; notificare către aceiași destinatari.
7. Job de reconciliere periodic: DB vs Exchange (acoperă restarturi, modificări manuale, joburi ratate).

## 6. Ecrane

1. **Dashboard**: Active / Programate / Expirate (+ în așteptare și oprite), căutare, filtre, paginare, acțiune „Oprește".
2. **Formular delegare**: câmpuri cerute, upload, câmpuri read-only (creat de, aprobat de, informați).
3. **Aprobare**: detaliu + Aprobă / Respinge (motiv).
4. **Administrare**: AD, Exchange, SMTP (cu „Test conexiune"), cale stocare fișiere (cu test de scriere), limită/extensii, mapare grupuri, template-uri, opțiunea de copie.
5. **Audit**: filtre (user, acțiune, entitate, interval), export CSV.
6. **i18n**: comutare ro/en în header; mesaje de eroare API și emailuri localizate (limba destinatarului sau cea implicită).

## 7. Securitate

- Secrete criptate (Data Protection/DPAPI); cont Exchange cu drepturi minime.
- Fișiere în afara webroot, nume generate, validare extensie/dimensiune, limite ridicate coerent în Kestrel, IIS și FormOptions.
- Autorizare per endpoint pe rol; CSRF; validare server-side.
- Regulă configurabilă: adminul nu își aprobă propria cerere.
- Audit pentru orice acțiune, inclusiv cele automate.

## 8. Etape

| Etapă | Conținut |
|---|---|
| 0 | Solution, CI, schema SQL, auth AD, layout React, i18n, middleware audit. |
| 1 | Servicii AD, Exchange, SMTP (cu interfețe + mock-uri) și ecran Administrare minim pentru test conexiuni. |
| 2 | Delegări: CRUD, validări, mașina de stări, atașamente. |
| 3 | Aprobare, bifa fără aprobare, notificări ro/en. |
| 4 | Scheduler: activare, expirare, oprire, reconciliere. |
| 5 | Dashboard. |
| 6 | Administrare completă și Audit. |
| 7 | Teste, UAT pe Exchange de test (cu feedback), runbook deploy. |

**Strategie de testare (fără acces la Exchange din dezvoltare):** integrările sunt în spatele interfețelor (`IExchangeService`, `IDirectoryService`, `IMailService`) cu implementări mock/in-memory pentru dezvoltare și teste automate. Validarea pe Exchange real o face utilizatorul, cu un script de smoke-test furnizat de noi (set/get/remove forward pe o căsuță de test).

## 9. Riscuri

- PowerShell remoting: pool de runspace-uri, timeout, permisiuni RBAC.
- Politici Exchange (transport rules / remote domains) pot interfera cu forwardul.
- Fus orar și DST: UTC în DB, afișare locală.
- Un forward preexistent pe sursă: se restaurează la final; trebuie confirmat comportamentul dorit.
- Cod nevalidat pe Exchange real până la feedbackul utilizatorului.

## 10. Puncte încă deschise (minore)

1. Dacă sursa are deja un forward setat: blocăm crearea sau îl suprascriem temporar și îl restaurăm? (propus: suprascriere + restaurare, cu avertisment în formular)
2. Copie în căsuța sursă: setare globală din administrare sau bifă per delegare? (propus: setare globală, implicit „da")
3. Cine sunt „cei informați": grupul de aprobatori, grupul de admini, utilizatorul sursă și destinatarul? (propus: toate patru)
4. Politică de retenție pentru fișiere și audit (ex. ștergere după N ani)?
