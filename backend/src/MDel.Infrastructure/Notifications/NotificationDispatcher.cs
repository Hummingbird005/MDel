using MDel.Application.Abstractions;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MDel.Infrastructure.Notifications;

/// <summary>
/// Trimite mesajele din coada outbox. Rulează în fundal (la semnal sau la fiecare 30 s);
/// la eroare reîncearcă cu pauze crescătoare (1, 2, 4, 8 minute), maxim <see cref="MaxAttempts"/> încercări.
/// Operațiile pe delegări nu depind de reușita emailului.
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IMailService mail,
    NotificationSignal signal,
    TimeProvider time,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    public const int MaxAttempts = 5;
    private const int BatchSize = 50;
    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Notification dispatch failed");
            }

            try
            {
                await signal.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Trimite tot ce e scadent. Public pentru teste și pentru jobul de reconciliere.</summary>
    public async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MDelDbContext>();
            var now = time.GetUtcNow().UtcDateTime;

            // Mesajele noi (fără încercări) primele, apoi cele mai vechi încercări: mesajele în pauză după erori
            // nu blochează trimiterea celor noi.
            var candidates = await db.DelegationNotifications
                .Where(n => n.SentUtc == null && n.Attempts < MaxAttempts)
                .OrderBy(n => n.LastAttemptUtc == null ? 0 : 1).ThenBy(n => n.LastAttemptUtc).ThenBy(n => n.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            var sent = 0;
            foreach (var n in candidates.Where(n => n.LastAttemptUtc is null ||
                         n.LastAttemptUtc.Value.AddMinutes(Math.Pow(2, n.Attempts - 1)) <= now))
            {
                n.Attempts++;
                n.LastAttemptUtc = now;
                try
                {
                    await mail.SendAsync(new MailMessage([n.RecipientEmail], n.Subject, n.HtmlBody), ct);
                    n.SentUtc = time.GetUtcNow().UtcDateTime;
                    n.Error = null;
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    n.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                    logger.LogWarning(ex, "Sending notification {Id} to {Email} failed (attempt {Attempt}/{Max})",
                        n.Id, n.RecipientEmail, n.Attempts, MaxAttempts);
                }

                // Salvat după fiecare mesaj: o oprire a aplicației în mijlocul lotului nu retrimite ce a plecat deja.
                await db.SaveChangesAsync(CancellationToken.None);
            }

            return sent;
        }
        finally
        {
            _gate.Release();
        }
    }
}
