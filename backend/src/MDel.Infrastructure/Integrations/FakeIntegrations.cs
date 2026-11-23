using System.Collections.Concurrent;
using MDel.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace MDel.Infrastructure.Integrations;

// Implementări in-memory pentru dezvoltare și teste, fără AD/Exchange/SMTP reale.
// Se activează cu "Integrations:UseFakes": true. Implementările reale vin în etapa 1.

public sealed class FakeDirectoryService : IDirectoryService
{
    private static readonly DirectoryUser[] Users =
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "dev.admin", "Dev Admin", "dev.admin@contoso.local", true),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "dev.approver", "Dev Approver", "dev.approver@contoso.local", true),
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), "ion.popescu", "Ion Popescu", "ion.popescu@contoso.local", true),
        new(Guid.Parse("00000000-0000-0000-0000-000000000004"), "maria.ionescu", "Maria Ionescu", "maria.ionescu@contoso.local", true),
        new(Guid.Parse("00000000-0000-0000-0000-000000000005"), "svc.nomailbox", "Service Account", null, false),
    ];

    public Task<IReadOnlyList<DirectoryUser>> SearchUsersAsync(string term, int maxResults, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DirectoryUser>>(Users
            .Where(u => u.SamAccountName.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || u.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToList());

    public Task<DirectoryUser?> FindBySamAccountNameAsync(string samAccountName, CancellationToken ct)
    {
        var sam = samAccountName.Contains('\\') ? samAccountName[(samAccountName.IndexOf('\\') + 1)..] : samAccountName;
        return Task.FromResult(Users.FirstOrDefault(u => u.SamAccountName.Equals(sam, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct) =>
        Task.FromResult(new ConnectionTestResult(true, "Fake directory (development)."));
}

public sealed class FakeExchangeService(ILogger<FakeExchangeService> logger) : IExchangeService
{
    private readonly ConcurrentDictionary<string, MailboxForwarding> _state = new(StringComparer.OrdinalIgnoreCase);

    public Task<MailboxForwarding> GetForwardingAsync(string mailboxIdentity, CancellationToken ct) =>
        Task.FromResult(_state.GetValueOrDefault(mailboxIdentity, new MailboxForwarding(null, false)));

    /// <summary>Pentru teste: simulează un forward preexistent.</summary>
    public void Seed(string mailbox, MailboxForwarding forwarding) => _state[mailbox] = forwarding;

    public Task SetForwardingAsync(string mailboxIdentity, string? forwardingAddress, bool deliverToMailboxAndForward, CancellationToken ct)
    {
        logger.LogInformation("[FAKE] Set-Mailbox {Mailbox} -ForwardingAddress {Target} -DeliverToMailboxAndForward {Keep}",
            mailboxIdentity, forwardingAddress ?? "$null", deliverToMailboxAndForward);
        _state[mailboxIdentity] = new MailboxForwarding(forwardingAddress, deliverToMailboxAndForward);
        return Task.CompletedTask;
    }

    public Task<bool> IsInternalRecipientAsync(string identity, CancellationToken ct) =>
        Task.FromResult(identity.EndsWith("@contoso.local", StringComparison.OrdinalIgnoreCase) || !identity.Contains('@'));

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct) =>
        Task.FromResult(new ConnectionTestResult(true, "Fake Exchange (development)."));
}

public sealed class FakeMailService(ILogger<FakeMailService> logger) : IMailService
{
    public ConcurrentQueue<MailMessage> Sent { get; } = new();

    public Task SendAsync(MailMessage message, CancellationToken ct)
    {
        logger.LogInformation("[FAKE] Mail to {To}: {Subject}", string.Join(", ", message.To), message.Subject);
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct) =>
        Task.FromResult(new ConnectionTestResult(true, "Fake SMTP (development)."));
}
