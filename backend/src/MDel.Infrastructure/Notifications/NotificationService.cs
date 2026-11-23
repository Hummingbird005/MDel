using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Domain.Entities;
using MDel.Domain.Enums;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MDel.Infrastructure.Notifications;

/// <summary>
/// Stabilește cine primește notificarea pentru un eveniment și pune mesajele în coada outbox
/// (DelegationNotifications). Nu salvează – apelantul face SaveChanges în aceeași tranzacție cu
/// schimbarea de stare, apoi <see cref="NotificationDispatcher"/> trimite emailurile.
///
/// Destinatari pe eveniment:
///   Created (cu aprobare)   – aprobatori (cerere de acțiune), grup de informare
///   Created (fără aprobare) – aprobatori (informare), administratori, sursă, destinatar, grup de informare
///   Approved / Activated / Expired / Stopped – administratori, sursă, destinatar, grup de informare
///   Rejected – administratori, grup de informare
///   Failed   – administratori
/// </summary>
public sealed class NotificationService(MDelDbContext db, ISettingsService settings, TimeProvider time, NotificationSignal signal)
{
    private const string NotConfigured = "(not configured)";

    public async Task EnqueueAsync(Delegation d, NotificationEvent ev, string? reason, CancellationToken ct)
    {
        await EnsureLoadedAsync(d, ct);

        var defaultLang = await settings.GetAsync(SettingKeys.DefaultLanguage, ct) ?? "ro";
        var baseUrl = await settings.GetAsync(SettingKeys.AppBaseUrl, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var recipients = await ResolveRecipientsAsync(d, ev, ct);

        foreach (var r in recipients.DistinctBy(r => r.Email, StringComparer.OrdinalIgnoreCase))
        {
            var lang = r.User?.PreferredLanguage ?? defaultLang;
            var actionRequired = ev == NotificationEvent.Created && d.RequiresApproval && r.Type == RecipientType.ApproverGroup;
            var (subject, body) = NotificationTemplates.Render(d, ev, actionRequired, lang, reason, baseUrl);
            var missing = r.Email == NotConfigured;

            d.Notifications.Add(new DelegationNotification
            {
                RecipientUserId = r.User?.Id,
                RecipientEmail = r.Email,
                RecipientType = r.Type,
                Event = ev,
                Language = lang,
                Subject = subject,
                HtmlBody = body,
                CreatedUtc = now,
                // O adresă de grup lipsă nu se poate trimite: rămâne vizibilă ca eroare, fără reîncercări.
                Attempts = missing ? NotificationDispatcher.MaxAttempts : 0,
                Error = missing ? $"{r.Type} email address is not configured in Administration." : null,
            });
        }

        signal.Notify();
    }

    private sealed record Recipient(string Email, RecipientType Type, AppUser? User);

    private async Task<List<Recipient>> ResolveRecipientsAsync(Delegation d, NotificationEvent ev, CancellationToken ct)
    {
        var list = new List<Recipient>();

        async Task AddGroup(string settingKey, RecipientType type)
        {
            var email = await settings.GetAsync(settingKey, ct);
            list.Add(new Recipient(string.IsNullOrWhiteSpace(email) ? NotConfigured : email.Trim(), type, null));
        }

        void AddUser(AppUser? user, RecipientType type)
        {
            if (!string.IsNullOrWhiteSpace(user?.Email)) list.Add(new Recipient(user.Email, type, user));
        }

        async Task AddInformGroup()
        {
            var members = await db.InformGroupMembers.AsNoTracking().Include(m => m.User)
                .Where(m => m.User.IsActive).Select(m => m.User).ToListAsync(ct);
            foreach (var m in members) AddUser(m, RecipientType.InformGroup);
        }

        switch (ev)
        {
            case NotificationEvent.Created when d.RequiresApproval:
                await AddGroup(SettingKeys.GroupApproversEmail, RecipientType.ApproverGroup);
                await AddInformGroup();
                break;

            case NotificationEvent.Created:
                await AddGroup(SettingKeys.GroupApproversEmail, RecipientType.ApproverGroup);
                await AddGroup(SettingKeys.GroupAdminsEmail, RecipientType.AdminGroup);
                AddUser(d.FromUser, RecipientType.SourceUser);
                AddUser(d.ToUser, RecipientType.TargetUser);
                await AddInformGroup();
                break;

            case NotificationEvent.Approved:
            case NotificationEvent.Activated:
            case NotificationEvent.Expired:
            case NotificationEvent.Stopped:
                await AddGroup(SettingKeys.GroupAdminsEmail, RecipientType.AdminGroup);
                AddUser(d.FromUser, RecipientType.SourceUser);
                AddUser(d.ToUser, RecipientType.TargetUser);
                await AddInformGroup();
                break;

            case NotificationEvent.Rejected:
                await AddGroup(SettingKeys.GroupAdminsEmail, RecipientType.AdminGroup);
                await AddInformGroup();
                break;

            case NotificationEvent.Failed:
                await AddGroup(SettingKeys.GroupAdminsEmail, RecipientType.AdminGroup);
                break;
        }

        return list;
    }

    private async Task EnsureLoadedAsync(Delegation d, CancellationToken ct)
    {
        var entry = db.Entry(d);
        if (entry.State == EntityState.Detached) return;
        foreach (var nav in new[] { nameof(Delegation.FromUser), nameof(Delegation.ToUser), nameof(Delegation.CreatedBy), nameof(Delegation.ApprovedBy) })
        {
            var reference = entry.Reference(nav);
            if (!reference.IsLoaded) await reference.LoadAsync(ct);
        }
    }
}

/// <summary>Trezește dispatcher-ul imediat după ce s-au pus mesaje în coadă.</summary>
public sealed class NotificationSignal
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Notify()
    {
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}
