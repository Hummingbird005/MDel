<#
.SYNOPSIS
    Pregătește Active Directory pentru MDel, înainte de prima pornire: contul de serviciu, cele trei grupuri
    (administratori delegări, aprobatori, administratori sistem – cu membrii lor), SPN-urile pentru Kerberos și,
    opțional, adresele de email ale grupurilor de administratori și aprobatori.

    Configurarea AD în aplicație (server, SSL, OU-uri, cont de citire) NU se face aici: se face din interfață,
    Setări → Active Directory, după prima pornire. Vezi docs/AD-SETUP.md.

.DESCRIPTION
    Idempotent: ce există deja nu se recreează (doar se completează membrii / SPN-urile lipsă).
    Rulați cu -WhatIf pentru a vedea ce s-ar face, fără nicio modificare.

    Cerințe: modulul ActiveDirectory (RSAT) și un cont cu drept de creare utilizatori/grupuri în OU.
    Pentru -MailEnableGroups: Exchange Management Shell (Enable-DistributionGroup).

.EXAMPLE
    .\New-MDelAdObjects.ps1 -OrganizationalUnit 'OU=MDel,OU=Servicii,DC=contoso,DC=local' `
        -AppHostName mdel.contoso.local -AdminMembers ion.popescu,maria.ionescu -ApproverMembers dan.georgescu `
        -SystemAdminMembers ion.popescu -WhatIf

.EXAMPLE
    # Din Exchange Management Shell (cu modulul ActiveDirectory importat), ca grupurile să primească adrese de email:
    .\New-MDelAdObjects.ps1 -OrganizationalUnit 'OU=MDel,DC=contoso,DC=local' -AppHostName mdel.contoso.local `
        -AdminMembers ion.popescu -ApproverMembers dan.georgescu -SystemAdminMembers ion.popescu -MailEnableGroups
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # OU în care se creează contul de serviciu și grupurile.
    [Parameter(Mandatory)] [string] $OrganizationalUnit,
    # Numele DNS al aplicației (ex. mdel.contoso.local) – pentru SPN-urile HTTP.
    [Parameter(Mandatory)] [string] $AppHostName,
    [string]   $ServiceAccount = 'svc-mdel',
    [string]   $AdminsGroup = 'MDel-Admins',
    [string]   $ApproversGroup = 'MDel-Approvers',
    [string]   $SystemAdminsGroup = 'MDel-System',
    # Conturi (sAMAccountName) adăugate în grupuri.
    [string[]] $AdminMembers = @(),
    [string[]] $ApproverMembers = @(),
    [string[]] $SystemAdminMembers = @(),
    # Grupurile devin grupuri de securitate cu adresă de email (notificările se trimit pe ele).
    [switch]   $MailEnableGroups,
    # Fără SPN-uri (ex. app pool-ul rulează ca ApplicationPoolIdentity / cont de calculator).
    [switch]   $SkipSpn
)

$ErrorActionPreference = 'Stop'
Import-Module ActiveDirectory

$domain = Get-ADDomain
$netbios = $domain.NetBIOSName
Write-Host "Domeniu: $($domain.DNSRoot) ($netbios)  OU: $OrganizationalUnit" -ForegroundColor Cyan
Get-ADOrganizationalUnit -Identity $OrganizationalUnit | Out-Null

# ------------------------------------------------------------------ 1. Contul de serviciu
$svc = Get-ADUser -Filter "sAMAccountName -eq '$ServiceAccount'" -Properties ServicePrincipalName
if (-not $svc) {
    if ($PSCmdlet.ShouldProcess($ServiceAccount, 'Creare cont de serviciu')) {
        $password = Read-Host "Parola pentru $netbios\$ServiceAccount" -AsSecureString
        New-ADUser -Name $ServiceAccount -SamAccountName $ServiceAccount `
            -UserPrincipalName "$ServiceAccount@$($domain.DNSRoot)" -Path $OrganizationalUnit `
            -Description 'MDel – identitatea app pool-ului IIS (SQL, LDAP, Exchange)' `
            -AccountPassword $password -Enabled $true -PasswordNeverExpires $true -CannotChangePassword $true
        $svc = Get-ADUser -Identity $ServiceAccount -Properties ServicePrincipalName
        Write-Host "  + cont creat: $netbios\$ServiceAccount" -ForegroundColor Green
    }
} else {
    Write-Host "  = contul $netbios\$ServiceAccount există"
}

# ------------------------------------------------------------------ 2. Grupuri + membri
function Initialize-MDelGroup([string] $name, [string] $description, [string[]] $members) {
    $group = Get-ADGroup -Filter "sAMAccountName -eq '$name'"
    if (-not $group) {
        if ($PSCmdlet.ShouldProcess($name, 'Creare grup de securitate')) {
            New-ADGroup -Name $name -SamAccountName $name -GroupScope Universal -GroupCategory Security `
                -Path $OrganizationalUnit -Description $description
            Write-Host "  + grup creat: $netbios\$name" -ForegroundColor Green
        }
    } else {
        Write-Host "  = grupul $netbios\$name există"
    }

    foreach ($member in $members) {
        $user = Get-ADUser -Identity $member
        $isMember = $group -and (Get-ADGroupMember -Identity $name -Recursive |
            Where-Object { $_.distinguishedName -eq $user.DistinguishedName })
        if ($isMember) {
            Write-Host "    = $member e deja membru"
        } elseif ($PSCmdlet.ShouldProcess("$name ← $member", 'Adăugare membru')) {
            Add-ADGroupMember -Identity $name -Members $user
            Write-Host "    + $member adăugat" -ForegroundColor Green
        }
    }
}

# Universal: necesar pentru a putea deveni grup cu adresă de email în Exchange.
Initialize-MDelGroup $AdminsGroup 'MDel – administratori delegări (creează și opresc delegări)' $AdminMembers
Initialize-MDelGroup $ApproversGroup 'MDel – aprobatori (aprobă / resping cererile de delegare)' $ApproverMembers
Initialize-MDelGroup $SystemAdminsGroup 'MDel – administratori sistem (Setări, Istoric / Audit)' $SystemAdminMembers

# ------------------------------------------------------------------ 3. SPN-uri (Kerberos)
# Necesare când app pool-ul rulează cu contul de serviciu și site-ul se deschide cu un nume DNS propriu.
if (-not $SkipSpn -and $svc) {
    $shortName = $AppHostName.Split('.')[0]
    foreach ($spn in @("HTTP/$AppHostName", "HTTP/$shortName")) {
        $owner = Get-ADObject -LDAPFilter "(servicePrincipalName=$spn)" -Properties sAMAccountName
        if ($owner -and $owner.sAMAccountName -ne $ServiceAccount) {
            Write-Warning "SPN $spn aparține deja lui $($owner.sAMAccountName) – Kerberos nu va funcționa până nu îl mutați (setspn -D)."
        } elseif ($owner) {
            Write-Host "  = SPN $spn există"
        } elseif ($PSCmdlet.ShouldProcess("$ServiceAccount ← $spn", 'Adăugare SPN')) {
            Set-ADUser -Identity $ServiceAccount -ServicePrincipalNames @{ Add = $spn }
            Write-Host "  + SPN $spn" -ForegroundColor Green
        }
    }
}

# ------------------------------------------------------------------ 4. Adrese de email pentru grupuri
$emails = @{}
foreach ($name in @($AdminsGroup, $ApproversGroup)) {
    if ($MailEnableGroups) {
        if (-not (Get-Command Enable-DistributionGroup -ErrorAction SilentlyContinue)) {
            throw 'Enable-DistributionGroup lipsește: rulați scriptul din Exchange Management Shell sau fără -MailEnableGroups.'
        }
        $recipient = Get-Recipient -Identity $name -ErrorAction SilentlyContinue
        if (-not $recipient -and $PSCmdlet.ShouldProcess($name, 'Activare email (Enable-DistributionGroup)')) {
            Enable-DistributionGroup -Identity $name | Out-Null
            # Notificările vin de la aplicație (expeditor intern): permitem mesaje doar din organizație.
            Set-DistributionGroup -Identity $name -RequireSenderAuthenticationEnabled $true
            $recipient = Get-Recipient -Identity $name
            Write-Host "  + $name are adresa $($recipient.PrimarySmtpAddress)" -ForegroundColor Green
        }
        if ($recipient) { $emails[$name] = "$($recipient.PrimarySmtpAddress)" }
    } else {
        $mail = (Get-ADGroup -Identity $name -Properties mail -ErrorAction SilentlyContinue).mail
        if ($mail) { $emails[$name] = $mail }
    }
}

# ------------------------------------------------------------------ 5. Ce urmează
$adminsEmail = if ($emails[$AdminsGroup]) { $emails[$AdminsGroup] } else { '' }
$approversEmail = if ($emails[$ApproversGroup]) { $emails[$ApproversGroup] } else { '' }
$firstAdmin = if ($SystemAdminMembers.Count -gt 0) { "$netbios\\$($SystemAdminMembers[0])" }
              elseif ($AdminMembers.Count -gt 0) { "$netbios\\$($AdminMembers[0])" }
              else { "$netbios\\<contul-dvs>" }

Write-Host ''
Write-Host 'Gata. În appsettings.json (folderul aplicației) completați doar adminul inițial:' -ForegroundColor Cyan
@"
  "Setup": {
    "InitialAdmins": [ "$firstAdmin" ]
  }
"@ | Write-Host

Write-Host ''
Write-Host 'După prima pornire, în aplicație, la Setări:' -ForegroundColor Cyan
Write-Host "  Active Directory : domeniul $($domain.DNSRoot), serverul (ex. un controler de domeniu), SSL, OU-urile de căutare, contul și parola de citire"
Write-Host "  Grupuri          : administratori delegări  = $netbios\$AdminsGroup   (email: $adminsEmail)"
Write-Host "                     aprobatori               = $netbios\$ApproversGroup   (email: $approversEmail)"
Write-Host "                     administratori sistem    = $netbios\$SystemAdminsGroup   (completați-l ultimul, după ce sunteți membru)"
Write-Host '  Contul și parola se salvează criptat în baza de date, nu în fișier.'

Write-Host ''
Write-Host "App pool-ul IIS: Identity = $netbios\$ServiceAccount. Membrii noi ai grupurilor trebuie să se deconecteze/reconecteze" `
    '(Windows) ca grupul să apară în tichetul Kerberos.' -ForegroundColor Yellow
