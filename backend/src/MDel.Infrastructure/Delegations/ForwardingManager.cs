using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Domain.Entities;
using MDel.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MDel.Infrastructure.Delegations;

/// <summary>
/// Aplică și elimină forwardul în Exchange pentru o delegare și actualizează statusul.
/// Nu salvează în baza de date – apelantul face SaveChanges.
/// Necesită ca FromUser și ToUser să fie încărcați.
/// </summary>
public sealed class ForwardingManager(
    IExchangeService exchange,
    IAuditService audit,
    TimeProvider time,
    ILogger<ForwardingManager> logger)
{
    /// <summary>Activează forwardul. Returnează false și trece delegarea în Failed la eroare.</summary>
    public async Task<bool> ApplyAsync(Delegation d, int? userId, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var source = MailboxIdentity(d.FromUser);
        var target = MailboxIdentity(d.ToUser);

        try
        {
            var current = await exchange.GetForwardingAsync(source, ct);

            // La reîncercare păstrăm valoarea anterioară salvată prima dată.
            if (d.ExchangeAppliedUtc is null)
            {
                d.PreviousForwardingAddress = current.ForwardingAddress;
                d.PreviousDeliverToMailboxAndForward = current.DeliverToMailboxAndForward;
            }

            await exchange.SetForwardingAsync(source, target, d.KeepCopyInSource, ct);
            var after = await exchange.GetForwardingAsync(source, ct);
            if (string.IsNullOrEmpty(after.ForwardingAddress))
            {
                throw new InvalidOperationException("Exchange did not report the forwarding address after Set-Mailbox.");
            }

            d.AppliedForwardingAddress = after.ForwardingAddress;
            d.ExchangeAppliedUtc = now;
            d.LastError = null;
            d.TransitionTo(DelegationStatus.Active, userId, now);

            await audit.WriteAsync(new AuditEntry("Delegation.Activated", nameof(Delegation), d.Id.ToString(),
                OldValues: new { current.ForwardingAddress, current.DeliverToMailboxAndForward },
                NewValues: new { after.ForwardingAddress, after.DeliverToMailboxAndForward }), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Activating delegation {Id} ({Source} -> {Target}) failed", d.Id, source, target);
            d.LastError = Truncate(ex.Message);
            d.UpdatedUtc = now; // folosit de scheduler pentru pauza dintre reîncercări
            if (DelegationStateMachine.CanTransition(d.Status, DelegationStatus.Failed))
            {
                d.TransitionTo(DelegationStatus.Failed, userId, now, Truncate(ex.Message, 1000));
            }

            await audit.WriteAsync(new AuditEntry("Delegation.ActivationFailed", nameof(Delegation), d.Id.ToString(),
                Details: ex.Message, Result: AuditResult.Failure), ct);
            return false;
        }
    }

    /// <summary>
    /// Elimină forwardul (restaurând valoarea anterioară) și trece delegarea în <paramref name="finalStatus"/>.
    /// Dacă forwardul a fost schimbat manual între timp, nu îl suprascriem.
    /// </summary>
    public async Task<bool> RemoveAsync(Delegation d, DelegationStatus finalStatus, int? userId, string? reason, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;

        if (d.ExchangeAppliedUtc is null || d.ExchangeRemovedUtc is not null)
        {
            d.TransitionTo(finalStatus, userId, now, reason);
            return true;
        }

        var source = MailboxIdentity(d.FromUser);
        try
        {
            var current = await exchange.GetForwardingAsync(source, ct);
            var stillOurs = string.Equals(current.ForwardingAddress, d.AppliedForwardingAddress, StringComparison.OrdinalIgnoreCase);

            if (stillOurs)
            {
                // Restaurare: starea de dinainte, oricare era (și un contact extern), fără regula „doar intern”.
                await exchange.RestoreForwardingAsync(source, d.PreviousForwardingAddress,
                    d.PreviousDeliverToMailboxAndForward ?? false, ct);
                d.LastError = null;
            }
            else
            {
                d.LastError = $"Forwarding was changed outside MDel (now '{current.ForwardingAddress ?? "none"}'); it was not restored.";
                logger.LogWarning("Delegation {Id}: {Message}", d.Id, d.LastError);
            }

            d.ExchangeRemovedUtc = now;
            d.TransitionTo(finalStatus, userId, now, reason);

            await audit.WriteAsync(new AuditEntry("Delegation.ForwardRemoved", nameof(Delegation), d.Id.ToString(),
                OldValues: new { current.ForwardingAddress },
                NewValues: new { RestoredTo = stillOurs ? d.PreviousForwardingAddress : "(not restored)", Status = finalStatus.ToString() }),
                ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Removing forwarding for delegation {Id} failed", d.Id);
            d.LastError = Truncate(ex.Message);
            d.UpdatedUtc = now;
            if (d.Status != DelegationStatus.Failed && DelegationStateMachine.CanTransition(d.Status, DelegationStatus.Failed))
            {
                d.TransitionTo(DelegationStatus.Failed, userId, now, Truncate(ex.Message, 1000));
            }

            await audit.WriteAsync(new AuditEntry("Delegation.RemoveFailed", nameof(Delegation), d.Id.ToString(),
                Details: ex.Message, Result: AuditResult.Failure), ct);
            return false;
        }
    }

    /// <summary>Exchange acceptă adresa SMTP primară ca identitate; fallback pe sAMAccountName.</summary>
    public static string MailboxIdentity(AppUser user) =>
        string.IsNullOrWhiteSpace(user.Email) ? user.SamAccountName : user.Email;

    private static string Truncate(string value, int max = 4000) => value.Length <= max ? value : value[..max];
}
