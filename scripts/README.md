# Scripturi AD și Exchange pentru MDel

Scripturile se rulează pe un server sau o stație din domeniu, în **Windows PowerShell 5.1**.

## 0. Obiectele AD (înainte de prima pornire)

```powershell
# modulul ActiveDirectory (RSAT); din Exchange Management Shell dacă folosiți -MailEnableGroups
.\New-MDelAdObjects.ps1 -OrganizationalUnit 'OU=MDel,DC=contoso,DC=local' -AppHostName mdel.contoso.local `
    -AdminMembers ion.popescu -ApproverMembers dan.georgescu -MailEnableGroups -WhatIf
```

Creează (doar ce lipsește) contul de serviciu `svc-mdel`, grupurile `MDel-Admins` / `MDel-Approvers` cu membrii,
SPN-urile `HTTP/<AppHostName>` și, cu `-MailEnableGroups`, adresele de email ale grupurilor. La final afișează
secțiunea `Setup` pentru `appsettings.json` (adminul inițial + setările AD). Fără `-WhatIf` aplică modificările.
Detalii: [docs/AD-SETUP.md](../docs/AD-SETUP.md).

## 1. Rol RBAC minim pentru contul de serviciu

```powershell
# în Exchange Management Shell, cu un cont din Organization Management
.\New-MDelRbacRole.ps1 -ServiceAccount CONTOSO\svc-mdel
```

Rolul `MDel Forwarding` permite doar `Get-Recipient`, `Get-Mailbox` și `Set-Mailbox`
(limitat la `-ForwardingAddress` și `-DeliverToMailboxAndForward`). Contul nu primește
acces la conținutul căsuțelor.

## 2. Smoke-test pe căsuțe de test

```powershell
.\Test-MDelExchange.ps1 -ExchangeServer ex01.contoso.local `
    -SourceMailbox test.sursa@contoso.local -TargetMailbox test.dest@contoso.local `
    -Credential (Get-Credential CONTOSO\svc-mdel) -WaitForMailTest
```

Scriptul face exact operațiile aplicației: verifică destinatarul ca fiind intern, salvează
forwardul existent, aplică forwardul, îl verifică, apoi **restaurează starea inițială**
(și la eroare). Rezultatele apar în consolă și într-un fișier CSV lângă script.

Ce merită testat:

| # | Scenariu | Parametri |
|---|---|---|
| 1 | Forward cu copie în sursă | `-KeepCopy $true` (implicit) |
| 2 | Forward fără copie | `-KeepCopy $false` |
| 3 | Sursă care are deja un forward | setați manual un forward înainte; verificați că e restaurat |
| 4 | Destinatar extern (contact/mail user) | `-TargetMailbox` = un contact extern → trebuie să dea FAIL la „Target is internal mailbox” |
| 5 | Contul de serviciu cu rolul minim | `-Credential` = contul de serviciu |

## 3. Testele din aplicație

În **Administrare**, fiecare tab are buton „Testează conexiunea”:

- **Active Directory**: bind LDAP + citirea Base DN.
- **Exchange**: `Get-Recipient -ResultSize 1` prin PowerShell remoting, plus verificarea forwardului
  curent al unei căsuțe (doar citire).
- **Email (SMTP)**: conectare (și autentificare, dacă e setat utilizator), opțional email de test.
- **Fișiere**: scriere, citire și ștergere a unui fișier de probă în calea configurată.

Vă rog să-mi trimiteți rezultatele (CSV-ul și mesajele din aplicație), mai ales erorile.
