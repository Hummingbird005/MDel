namespace MDel.Application.Abstractions;

public sealed record DirectoryUser(
    Guid ObjectId,
    string SamAccountName,
    string DisplayName,
    string? Email,
    bool HasMailbox);

/// <summary>Acces la Active Directory (căutare utilizatori, membership grupuri).</summary>
public interface IDirectoryService
{
    Task<IReadOnlyList<DirectoryUser>> SearchUsersAsync(string term, int maxResults, CancellationToken ct);
    Task<DirectoryUser?> FindBySamAccountNameAsync(string samAccountName, CancellationToken ct);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
}

public sealed record MailboxForwarding(string? ForwardingAddress, bool DeliverToMailboxAndForward);

/// <summary>
/// Operații Exchange 2016 pentru forward intern (Set-Mailbox -ForwardingAddress).
/// Nu acordă drepturi pe inbox.
/// </summary>
public interface IExchangeService
{
    Task<MailboxForwarding> GetForwardingAsync(string mailboxIdentity, CancellationToken ct);
    Task SetForwardingAsync(string mailboxIdentity, string? forwardingAddress, bool deliverToMailboxAndForward, CancellationToken ct);
    Task<bool> IsInternalRecipientAsync(string identity, CancellationToken ct);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
}

public sealed record MailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string HtmlBody,
    IReadOnlyList<string>? Cc = null);

public interface IMailService
{
    Task SendAsync(MailMessage message, CancellationToken ct);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);
}

public sealed record ConnectionTestResult(bool Success, string Message);
