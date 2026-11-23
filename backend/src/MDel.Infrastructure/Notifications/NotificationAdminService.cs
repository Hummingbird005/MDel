using MDel.Application.Abstractions;
using MDel.Domain.Entities;
using MDel.Domain.Enums;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MDel.Infrastructure.Notifications;

public enum OutboxFilter { Failed, Pending, Sent, All }

public sealed record OutboxItem(
    long Id,
    int DelegationId,
    string RecipientEmail,
    string? RecipientName,
    RecipientType RecipientType,
    NotificationEvent Event,
    string Subject,
    DateTime CreatedUtc,
    DateTime? SentUtc,
    int Attempts,
    DateTime? LastAttemptUtc,
    string? Error);

/// <summary>Coada de emailuri pentru administratori: ce n-a plecat, de ce, și retrimiterea.</summary>
public sealed class NotificationAdminService(MDelDbContext db, NotificationSignal signal, IAuditService audit)
{
    public async Task<IReadOnlyList<OutboxItem>> ListAsync(OutboxFilter filter, CancellationToken ct)
    {
        var q = db.DelegationNotifications.AsNoTracking();
        q = filter switch
        {
            OutboxFilter.Failed => q.Where(n => n.SentUtc == null && n.Attempts >= NotificationDispatcher.MaxAttempts),
            OutboxFilter.Pending => q.Where(n => n.SentUtc == null && n.Attempts < NotificationDispatcher.MaxAttempts),
            OutboxFilter.Sent => q.Where(n => n.SentUtc != null),
            _ => q,
        };

        return await q.OrderByDescending(n => n.Id).Take(200)
            .Select(n => new OutboxItem(n.Id, n.DelegationId, n.RecipientEmail, n.RecipientUser != null ? n.RecipientUser.DisplayName : null,
                n.RecipientType, n.Event, n.Subject, n.CreatedUtc, n.SentUtc, n.Attempts, n.LastAttemptUtc, n.Error))
            .ToListAsync(ct);
    }

    public async Task<int> CountFailedAsync(CancellationToken ct) =>
        await db.DelegationNotifications.CountAsync(n => n.SentUtc == null && n.Attempts >= NotificationDispatcher.MaxAttempts, ct);

    /// <summary>
    /// Repune mesajul în coadă. Pentru adresele de grup, destinatarul se recitește din setări
    /// (util după ce adminul a completat o adresă lipsă).
    /// </summary>
    public async Task<bool> RetryAsync(long id, ISettingsService settings, CancellationToken ct)
    {
        var n = await db.DelegationNotifications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n is null || n.SentUtc is not null) return false;

        var groupKey = n.RecipientType switch
        {
            RecipientType.ApproverGroup => Domain.SettingKeys.GroupApproversEmail,
            RecipientType.AdminGroup => Domain.SettingKeys.GroupAdminsEmail,
            _ => null,
        };
        if (groupKey is not null && await settings.GetAsync(groupKey, ct) is { Length: > 0 } email)
        {
            n.RecipientEmail = email.Trim();
        }

        var old = new { n.RecipientEmail, n.Attempts, n.Error };
        n.Attempts = 0;
        n.LastAttemptUtc = null;
        n.Error = null;
        await db.SaveChangesAsync(ct);
        signal.Notify();

        await audit.WriteAsync(new AuditEntry("Notification.Retried", nameof(DelegationNotification), id.ToString(),
            OldValues: old, NewValues: new { n.RecipientEmail }), ct);
        return true;
    }
}
