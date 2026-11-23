<#
.SYNOPSIS
    Smoke-test MDel pe Exchange 2016: setează, verifică și restaurează forwardul unei căsuțe de test.

.DESCRIPTION
    Reproduce exact operațiile pe care le face aplicația MDel (ExchangePowerShellService):
      1. conectare PowerShell remoting la http://<server>/PowerShell/ (Kerberos)
      2. Get-Recipient pe destinatar  -> trebuie să fie căsuță internă
      3. Get-Mailbox pe sursă         -> salvează forwardul existent
      4. Set-Mailbox -ForwardingAddress <destinatar> -DeliverToMailboxAndForward <copie>
      5. Get-Mailbox                  -> verifică aplicarea
      6. (opțional) pauză pentru a trimite manual un email de test către sursă
      7. Set-Mailbox                  -> restaurează forwardul inițial (rulează și la eroare)
      8. Get-Mailbox                  -> verifică restaurarea
    Folosiți DOAR căsuțe de test.

.EXAMPLE
    .\Test-MDelExchange.ps1 -ExchangeServer ex01.contoso.local -SourceMailbox test.sursa@contoso.local -TargetMailbox test.dest@contoso.local

.EXAMPLE
    # Cu contul de serviciu al aplicației și pauză pentru test manual de mail
    .\Test-MDelExchange.ps1 -ExchangeServer ex01.contoso.local -SourceMailbox test.sursa -TargetMailbox test.dest `
        -Credential (Get-Credential CONTOSO\svc-mdel) -WaitForMailTest
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ExchangeServer,
    [Parameter(Mandatory)] [string] $SourceMailbox,
    [Parameter(Mandatory)] [string] $TargetMailbox,
    [bool] $KeepCopy = $true,
    [pscredential] $Credential,
    [switch] $WaitForMailTest,
    [switch] $UseHttps
)

$ErrorActionPreference = 'Stop'
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result([string] $Step, [bool] $Ok, [string] $Detail) {
    $results.Add([pscustomobject]@{ Step = $Step; Result = $(if ($Ok) { 'PASS' } else { 'FAIL' }); Detail = $Detail })
    $color = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ("[{0}] {1} - {2}" -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Step, $Detail) -ForegroundColor $color
}

$scheme = if ($UseHttps) { 'https' } else { 'http' }
$uri = "${scheme}://$ExchangeServer/PowerShell/"
$session = $null
$previous = $null
$changed = $false

try {
    # 1. Conectare
    $sessionArgs = @{ ConfigurationName = 'Microsoft.Exchange'; ConnectionUri = $uri; Authentication = 'Kerberos' }
    if ($Credential) { $sessionArgs.Credential = $Credential }
    $session = New-PSSession @sessionArgs
    Import-PSSession $session -CommandName Get-Mailbox, Set-Mailbox, Get-Recipient -AllowClobber -DisableNameChecking | Out-Null
    Add-Result 'Connect' $true "Connected to $uri as $(if ($Credential) { $Credential.UserName } else { "$env:USERDOMAIN\$env:USERNAME" })"

    # 2. Destinatarul trebuie să fie căsuță internă
    $target = Get-Recipient -Identity $TargetMailbox
    $internal = $target.RecipientTypeDetails -in 'UserMailbox', 'SharedMailbox', 'LinkedMailbox'
    Add-Result 'Target is internal mailbox' $internal "$($target.PrimarySmtpAddress) ($($target.RecipientTypeDetails))"
    if (-not $internal) { throw "Target '$TargetMailbox' is not an internal mailbox." }

    # 3. Starea inițială a sursei
    $previous = Get-Mailbox -Identity $SourceMailbox
    $prevText = if ($previous.ForwardingAddress) { "$($previous.ForwardingAddress)" } else { '(none)' }
    Add-Result 'Read source mailbox' $true ("{0}: ForwardingAddress={1}; DeliverToMailboxAndForward={2}; ForwardingSmtpAddress={3}" -f `
        $previous.PrimarySmtpAddress, $prevText, $previous.DeliverToMailboxAndForward, $(if ($previous.ForwardingSmtpAddress) { $previous.ForwardingSmtpAddress } else { '(none)' }))
    if ($previous.ForwardingAddress) {
        Write-Warning "Source already has a forward to '$prevText'. It will be restored at the end (same behaviour as MDel)."
    }

    # 4. Aplicare forward
    Set-Mailbox -Identity $SourceMailbox -ForwardingAddress $TargetMailbox -DeliverToMailboxAndForward $KeepCopy
    $changed = $true

    # 5. Verificare
    $after = Get-Mailbox -Identity $SourceMailbox
    $applied = ($after.ForwardingAddress -ne $null) -and ($after.DeliverToMailboxAndForward -eq $KeepCopy)
    Add-Result 'Apply forwarding' $applied "ForwardingAddress=$($after.ForwardingAddress); DeliverToMailboxAndForward=$($after.DeliverToMailboxAndForward)"

    # 6. Test manual de mail
    if ($WaitForMailTest) {
        Write-Host ""
        Write-Host "Trimiteti acum un email catre $($previous.PrimarySmtpAddress) si verificati ca ajunge la $($target.PrimarySmtpAddress)" -ForegroundColor Cyan
        if ($KeepCopy) { Write-Host "(si ca o copie ramane in casuta sursa)" -ForegroundColor Cyan }
        $answer = Read-Host "Mailul a ajuns la destinatar? (y/n)"
        Add-Result 'Manual mail delivery' ($answer -match '^(y|d)') "Answer: $answer"
    }
}
catch {
    Add-Result 'Error' $false $_.Exception.Message
}
finally {
    # 7-8. Restaurare (și în caz de eroare)
    if ($changed -and $previous) {
        try {
            $restoreTarget = if ($previous.ForwardingAddress) { "$($previous.ForwardingAddress)" } else { $null }
            Set-Mailbox -Identity $SourceMailbox -ForwardingAddress $restoreTarget -DeliverToMailboxAndForward ([bool]$previous.DeliverToMailboxAndForward)
            $restored = Get-Mailbox -Identity $SourceMailbox
            $ok = ("$($restored.ForwardingAddress)" -eq "$($previous.ForwardingAddress)") -and `
                  ($restored.DeliverToMailboxAndForward -eq $previous.DeliverToMailboxAndForward)
            Add-Result 'Restore previous forwarding' $ok "ForwardingAddress=$(if ($restored.ForwardingAddress) { $restored.ForwardingAddress } else { '(none)' }); DeliverToMailboxAndForward=$($restored.DeliverToMailboxAndForward)"
        }
        catch {
            Add-Result 'Restore previous forwarding' $false "RESTORE FAILED – fix manually: $($_.Exception.Message)"
        }
    }

    if ($session) { Remove-PSSession $session }

    Write-Host ""
    $results | Format-Table -AutoSize -Wrap
    $logFile = Join-Path $PSScriptRoot ("mdel-exchange-test-{0:yyyyMMdd-HHmmss}.csv" -f (Get-Date))
    $results | Export-Csv -Path $logFile -NoTypeInformation -Encoding UTF8
    Write-Host "Results saved to $logFile"
}
