using System.Management.Automation;
using System.Management.Automation.Remoting;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security;
using MDel.Application.Abstractions;
using MDel.Domain;
using Microsoft.Extensions.Logging;

namespace MDel.Infrastructure.Integrations;

/// <summary>
/// Exchange 2016 on-premise prin PowerShell remoting (WinRM, endpoint-ul /PowerShell/ cu
/// ConfigurationName Microsoft.Exchange, autentificare Kerberos).
///
/// Cmdlet-uri folosite (compatibile cu un rol RBAC minim – vezi scripts/README.md):
///   Get-Mailbox, Set-Mailbox (-ForwardingAddress, -DeliverToMailboxAndForward), Get-Recipient.
///
/// Endpoint-ul Exchange rulează în NoLanguage mode, de aceea folosim doar AddCommand/AddParameter,
/// niciodată scripturi text – ceea ce elimină și riscul de injecție.
/// </summary>
public sealed class ExchangePowerShellService(ISettingsService settings, ILogger<ExchangePowerShellService> logger)
    : IExchangeService
{
    private const string ExchangeShellUri = "http://schemas.microsoft.com/powershell/Microsoft.Exchange";
    private const int MaxAttempts = 3;

    /// <summary>Tipuri de destinatar acceptate ca țintă de forward intern.</summary>
    private static readonly HashSet<string> InternalMailboxTypes =
        new(StringComparer.OrdinalIgnoreCase) { "UserMailbox", "SharedMailbox", "LinkedMailbox" };

    public async Task<MailboxForwarding> GetForwardingAsync(string mailboxIdentity, CancellationToken ct)
    {
        var results = await InvokeAsync("Get-Mailbox", new Dictionary<string, object?> { ["Identity"] = mailboxIdentity }, ct);
        var mailbox = results.SingleOrDefault()
            ?? throw new InvalidOperationException($"Mailbox '{mailboxIdentity}' was not found in Exchange.");

        return new MailboxForwarding(
            ToNullableString(mailbox.Properties["ForwardingAddress"]?.Value),
            mailbox.Properties["DeliverToMailboxAndForward"]?.Value is bool b && b,
            ToNullableString(mailbox.Properties["ForwardingSmtpAddress"]?.Value));
    }

    public async Task SetForwardingAsync(string mailboxIdentity, string? forwardingAddress, bool deliverToMailboxAndForward, CancellationToken ct)
    {
        if (forwardingAddress is not null && !await IsInternalRecipientAsync(forwardingAddress, ct))
        {
            throw new InvalidOperationException($"'{forwardingAddress}' is not an internal mailbox; only internal forwarding is allowed.");
        }

        await InvokeAsync("Set-Mailbox", new Dictionary<string, object?>
        {
            ["Identity"] = mailboxIdentity,
            ["ForwardingAddress"] = forwardingAddress, // null => elimină forwardul
            ["DeliverToMailboxAndForward"] = forwardingAddress is not null && deliverToMailboxAndForward,
        }, ct);

        logger.LogInformation("Exchange forwarding set: {Mailbox} -> {Target} (keep copy: {Keep})",
            mailboxIdentity, forwardingAddress ?? "(none)", deliverToMailboxAndForward);
    }

    public async Task<bool> IsInternalRecipientAsync(string identity, CancellationToken ct)
    {
        try
        {
            var results = await InvokeAsync("Get-Recipient", new Dictionary<string, object?> { ["Identity"] = identity }, ct);
            var recipient = results.SingleOrDefault();
            var type = ToNullableString(recipient?.Properties["RecipientTypeDetails"]?.Value);
            return type is not null && InternalMailboxTypes.Contains(type);
        }
        catch (ExchangeCommandException ex) when (ex.IsNotFound)
        {
            return false;
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var results = await InvokeAsync("Get-Recipient", new Dictionary<string, object?> { ["ResultSize"] = 1 }, ct);
            var uri = await settings.GetAsync(SettingKeys.ExchangePowerShellUri, ct);
            return new ConnectionTestResult(true, $"Connected to Exchange at {uri} (Get-Recipient returned {results.Count} item).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exchange connection test failed");
            return new ConnectionTestResult(false, $"Exchange connection failed: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ infrastructură

    private async Task<IReadOnlyList<PSObject>> InvokeAsync(string command, IDictionary<string, object?> parameters, CancellationToken ct)
    {
        var connectionInfo = await CreateConnectionInfoAsync(ct);

        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await Task.Run(() => InvokeOnce(connectionInfo, command, parameters, ct), ct);
            }
            catch (PSRemotingTransportException ex) when (attempt < MaxAttempts)
            {
                // Erori de transport (WinRM indisponibil temporar) – reîncercăm cu backoff.
                logger.LogWarning(ex, "Exchange transport error on {Command}, attempt {Attempt}/{Max}", command, attempt, MaxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
    }

    private static IReadOnlyList<PSObject> InvokeOnce(WSManConnectionInfo connectionInfo, string command,
        IDictionary<string, object?> parameters, CancellationToken ct)
    {
        using var runspace = RunspaceFactory.CreateRunspace(connectionInfo);
        runspace.Open();

        using var ps = PowerShell.Create();
        ps.Runspace = runspace;
        ps.AddCommand(command);
        foreach (var (name, value) in parameters)
        {
            ps.AddParameter(name, value);
        }

        using var registration = ct.Register(() => ps.Stop());
        var results = ps.Invoke();

        if (ps.HadErrors && ps.Streams.Error.Count > 0)
        {
            var error = ps.Streams.Error[0];
            throw new ExchangeCommandException(command, error.ToString(), error.CategoryInfo.Reason,
                error.FullyQualifiedErrorId);
        }

        return results.Where(r => r is not null).ToList();
    }

    private async Task<WSManConnectionInfo> CreateConnectionInfoAsync(CancellationToken ct)
    {
        var uriText = await settings.GetAsync(SettingKeys.ExchangePowerShellUri, ct);
        if (string.IsNullOrWhiteSpace(uriText) || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Exchange is not configured: set the PowerShell URI in Administration.");
        }

        var user = await settings.GetSecretAsync(SettingKeys.ExchangeServiceUser, ct); // utilizator criptat în baza de date
        PSCredential? credential = null;
        if (!string.IsNullOrWhiteSpace(user))
        {
            var password = await settings.GetSecretAsync(SettingKeys.ExchangeServicePassword, ct) ?? string.Empty;
            credential = new PSCredential(user, ToSecureString(password));
        }

        // Fără credențiale explicite => Kerberos cu identitatea procesului (app pool-ul IIS).
        return new WSManConnectionInfo(uri, ExchangeShellUri, credential)
        {
            AuthenticationMechanism = AuthenticationMechanism.Kerberos,
            OpenTimeout = 30_000,
            OperationTimeout = 60_000,
            IdleTimeout = 60_000,
            SkipCACheck = false,
            SkipCNCheck = false,
        };
    }

    private static SecureString ToSecureString(string value)
    {
        var secure = new NetworkCredential(string.Empty, value).SecurePassword;
        secure.MakeReadOnly();
        return secure;
    }

    private static string? ToNullableString(object? value)
    {
        var text = value switch
        {
            null => null,
            PSObject pso => pso.BaseObject?.ToString(),
            _ => value.ToString(),
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

public sealed class ExchangeCommandException(string command, string message, string? reason, string? errorId)
    : Exception($"{command} failed: {message}")
{
    public string Command { get; } = command;
    public string? Reason { get; } = reason;
    public string? ErrorId { get; } = errorId;

    /// <summary>Exchange raportează „couldn't be found” cu reason ManagementObjectNotFoundException.</summary>
    public bool IsNotFound =>
        (Reason?.Contains("NotFound", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (ErrorId?.Contains("NotFound", StringComparison.OrdinalIgnoreCase) ?? false);
}
