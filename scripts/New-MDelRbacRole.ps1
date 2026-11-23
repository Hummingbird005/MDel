<#
.SYNOPSIS
    Creează în Exchange 2016 un rol RBAC minim pentru contul de serviciu MDel.

.DESCRIPTION
    Rolul „MDel Forwarding” derivă din „Mail Recipients” și păstrează doar:
      - Get-Recipient, Get-Mailbox (citire)
      - Set-Mailbox limitat la parametrii Identity, ForwardingAddress, DeliverToMailboxAndForward
    Contul NU primește drepturi pe conținutul căsuțelor (fără Full Access / Send As).

    Rulați în Exchange Management Shell, cu un cont din „Organization Management”.
    Verificați rezultatul în mediul de test înainte de producție.

.EXAMPLE
    .\New-MDelRbacRole.ps1 -ServiceAccount CONTOSO\svc-mdel
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $ServiceAccount,
    [string] $RoleName = 'MDel Forwarding'
)

$ErrorActionPreference = 'Stop'
$keep = 'Get-Recipient', 'Get-Mailbox', 'Set-Mailbox'

if (-not (Get-ManagementRole -Identity $RoleName -ErrorAction SilentlyContinue)) {
    New-ManagementRole -Name $RoleName -Parent 'Mail Recipients' | Out-Null
    Write-Host "Role '$RoleName' created."
}

# Elimină toate cmdlet-urile în afară de cele necesare.
Get-ManagementRoleEntry "$RoleName\*" |
    Where-Object { $_.Name -notin $keep } |
    ForEach-Object { Remove-ManagementRoleEntry -Identity "$RoleName\$($_.Name)" -Confirm:$false }

# Set-Mailbox: doar parametrii de forward.
Set-ManagementRoleEntry -Identity "$RoleName\Set-Mailbox" -Parameters Identity, ForwardingAddress, DeliverToMailboxAndForward

# Atribuire către contul de serviciu.
$assignment = "$RoleName-$($ServiceAccount -replace '.*\\', '')"
if (-not (Get-ManagementRoleAssignment -Identity $assignment -ErrorAction SilentlyContinue)) {
    New-ManagementRoleAssignment -Name $assignment -Role $RoleName -User $ServiceAccount | Out-Null
}

# Contul trebuie să aibă PowerShell remoting activ în Exchange.
Set-User -Identity $ServiceAccount -RemotePowerShellEnabled $true

Write-Host "Done. Role entries:"
Get-ManagementRoleEntry "$RoleName\*" | Format-Table Name, Parameters -AutoSize -Wrap
