using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Domain.Entities;
using MDel.Domain.Enums;
using MDel.Infrastructure.Delegations;
using MDel.Infrastructure.Notifications;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MDel.Infrastructure.Scheduling;

public sealed record SchedulerRunResult(int Activated, int Expired, int Retried, int Failed);

/// <summary>
/// Joburile automate pentru delegări (rulate de <see cref="SchedulerHostedService"/>):
///  - activare: Scheduled cu StartUtc atins → forward aplicat în Exchange → Active (+ notificare)
///  - expirare: Active cu EndUtc atins → forward eliminat și cel anterior restaurat → Expired (+ notificare
///    către administratori, sursă, destinatar și grupul de informare); cererile neactivate expiră fără Exchange
///  - reîncercare: Failed – activare sau eliminare reîncercată la fiecare <see cref="RetryDelay"/>
///  - reconciliere: verifică în Exchange că forwardurile active sunt încă cele aplicate de aplicație
/// Fiecare delegare se salvează separat, ca o eroare să nu blocheze restul.
/// </summary>
public sealed class DelegationScheduler(
    MDelDbContext db,
    ForwardingManager forwarding,
    NotificationService notifications,
    IExchangeService exchange,
    IAuditService audit,
    TimeProvider time,
    ILogger<DelegationScheduler> logger)
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(10);
    private const int BatchSize = 100;

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<SchedulerRunResult> RunAsync(CancellationToken ct)
    {
        int activated = 0, expired = 0, retried = 0, failed = 0;
        var now = Now;

        // 1. Expirare (înainte de activare: o delegare cu start și sfârșit în trecut nu se mai activează).
        foreach (var id in await DueIdsAsync(d =>
                     (d.Status == DelegationStatus.Active || d.Status == DelegationStatus.Scheduled ||
                      d.Status == DelegationStatus.PendingApproval) && d.EndUtc <= now, ct))
        {
            var ok = await ProcessAsync(id, async d =>
            {
                var wasActive = d.Status == DelegationStatus.Active;
                var removed = await forwarding.RemoveAsync(d, DelegationStatus.Expired, null, ReasonExpired(d), ct);
                if (!removed)
                {
                    await notifications.EnqueueAsync(d, NotificationEvent.Failed, d.LastError, ct);
                    return false;
                }

                // Cele care n-au ajuns active (neaprobate / programate) nu au ce anunța utilizatorilor.
                if (wasActive) await notifications.EnqueueAsync(d, NotificationEvent.Expired, null, ct);
                return true;
            }, ct);
            if (ok) expired++; else failed++;
        }

        // 2. Activare.
        foreach (var id in await DueIdsAsync(d => d.Status == DelegationStatus.Scheduled && d.StartUtc <= now && d.EndUtc > now, ct))
        {
            var ok = await ProcessAsync(id, async d =>
            {
                if (await ForwardingGuard.SourceHasOtherAppliedForwardAsync(db, d, ct))
                {
                    logger.LogInformation("Delegation {Id} waits: another forward on the same mailbox is still applied", d.Id);
                    return true;
                }

                if (await forwarding.ApplyAsync(d, null, ct))
                {
                    await notifications.EnqueueAsync(d, NotificationEvent.Activated, null, ct);
                    return true;
                }

                await notifications.EnqueueAsync(d, NotificationEvent.Failed, d.LastError, ct);
                return false;
            }, ct);
            if (ok) activated++; else failed++;
        }

        // 3. Reîncercări pentru Failed (fără notificări repetate – adminii au fost anunțați la prima eroare).
        var retryBefore = now - RetryDelay;
        foreach (var id in await DueIdsAsync(d => d.Status == DelegationStatus.Failed &&
                     (d.UpdatedUtc == null || d.UpdatedUtc <= retryBefore), ct))
        {
            var ok = await ProcessAsync(id, async d =>
            {
                var applied = d.ExchangeAppliedUtc is not null && d.ExchangeRemovedUtc is null;
                if (applied && (d.StoppedUtc is not null || d.EndUtc <= now))
                {
                    var final = d.StoppedUtc is not null ? DelegationStatus.Stopped : DelegationStatus.Expired;
                    var reason = final == DelegationStatus.Stopped ? d.StopReason : ReasonExpired(d);
                    if (!await forwarding.RemoveAsync(d, final, null, reason, ct)) return false;
                    await notifications.EnqueueAsync(d, final == DelegationStatus.Stopped ? NotificationEvent.Stopped : NotificationEvent.Expired, reason, ct);
                    return true;
                }

                if (!applied && d.EndUtc <= now)
                {
                    d.TransitionTo(DelegationStatus.Expired, null, now, ReasonExpired(d));
                    return true;
                }

                if (!applied && d.StartUtc <= now)
                {
                    if (await ForwardingGuard.SourceHasOtherAppliedForwardAsync(db, d, ct)) return true;
                    if (!await forwarding.ApplyAsync(d, null, ct)) return false;
                    await notifications.EnqueueAsync(d, NotificationEvent.Activated, null, ct);
                    return true;
                }

                return true; // nimic de făcut încă
            }, ct);
            if (ok) retried++; else failed++;
        }

        if (activated + expired + retried + failed > 0)
        {
            logger.LogInformation("Scheduler run: {Activated} activated, {Expired} expired, {Retried} retried, {Failed} failed",
                activated, expired, retried, failed);
        }

        return new SchedulerRunResult(activated, expired, retried, failed);
    }

    /// <summary>
    /// Verifică în Exchange forwardurile active. Dacă cineva l-a schimbat manual, delegarea primește un
    /// avertisment, se auditează și administratorii sunt anunțați o singură dată.
    /// </summary>
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        var drifted = 0;
        var ids = await db.Delegations.AsNoTracking()
            .Where(d => d.Status == DelegationStatus.Active && d.ExchangeAppliedUtc != null)
            .Select(d => d.Id).ToListAsync(ct);

        foreach (var id in ids)
        {
            await ProcessAsync(id, async d =>
            {
                var current = await exchange.GetForwardingAsync(ForwardingManager.MailboxIdentity(d.FromUser), ct);
                if (string.Equals(current.ForwardingAddress, d.AppliedForwardingAddress, StringComparison.OrdinalIgnoreCase))
                {
                    if (d.LastError?.StartsWith(DriftPrefix, StringComparison.Ordinal) == true) d.LastError = null;
                    return true;
                }

                var message = $"{DriftPrefix} '{current.ForwardingAddress ?? "none"}' (expected '{d.AppliedForwardingAddress}').";
                if (d.LastError == message) return true; // deja raportat

                d.LastError = message;
                d.UpdatedUtc = Now;
                drifted++;
                await audit.WriteAsync(new AuditEntry("Delegation.DriftDetected", nameof(Delegation), d.Id.ToString(),
                    Details: message, Result: AuditResult.Failure), ct);
                await notifications.EnqueueAsync(d, NotificationEvent.Failed, message, ct);
                return true;
            }, ct);
        }

        return drifted;
    }

    private const string DriftPrefix = "Forwarding changed outside MDel: now";

    private static string ReasonExpired(Delegation d) => d.Status switch
    {
        DelegationStatus.PendingApproval => "Not approved before the end date",
        DelegationStatus.Scheduled => "End date reached before activation",
        _ => "End date reached",
    };

    private async Task<List<int>> DueIdsAsync(System.Linq.Expressions.Expression<Func<Delegation, bool>> filter, CancellationToken ct) =>
        await db.Delegations.AsNoTracking().Where(filter).OrderBy(d => d.Id).Select(d => d.Id).Take(BatchSize).ToListAsync(ct);

    /// <summary>Încarcă delegarea, rulează acțiunea și salvează; erorile și conflictele se loghează, fără a opri restul.</summary>
    private async Task<bool> ProcessAsync(int id, Func<Delegation, Task<bool>> action, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var d = await db.Delegations.Include(x => x.FromUser).Include(x => x.ToUser).Include(x => x.CreatedBy)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return true;

        try
        {
            var ok = await action(d);
            await db.SaveChangesAsync(ct);
            return ok;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Un utilizator a modificat delegarea în același timp; o reluăm la următoarea rulare.
            logger.LogInformation("Delegation {Id} changed concurrently; skipped in this run", id);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduler failed for delegation {Id}", id);
            return false;
        }
    }
}
