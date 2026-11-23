using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MDel.Infrastructure.Scheduling;

/// <summary>
/// Rulează jobul de delegări la fiecare <c>Scheduler:IntervalSeconds</c> (implicit 30 s), reconcilierea cu Exchange
/// la fiecare <c>Scheduler:ReconcileMinutes</c> (implicit 60) și retenția o dată pe zi.
/// Starea e în baza de date, deci un restart nu pierde nimic: la pornire se recuperează tot ce a devenit scadent.
/// Presupune o singură instanță a aplicației (IIS, un singur worker process).
/// </summary>
public sealed class SchedulerHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<SchedulerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Scheduler:Enabled", true))
        {
            logger.LogWarning("Scheduler is disabled (Scheduler:Enabled = false)");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue("Scheduler:IntervalSeconds", 30)));
        var reconcileEvery = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Scheduler:ReconcileMinutes", 60)));
        DateTime? lastReconcile = null, lastRetention = null;

        using var timer = new PeriodicTimer(interval, time);
        do
        {
            var now = time.GetUtcNow().UtcDateTime;
            await RunSafeAsync<DelegationScheduler>("delegations", s => s.RunAsync(stoppingToken), stoppingToken);

            if (lastReconcile is null || now - lastReconcile >= reconcileEvery)
            {
                await RunSafeAsync<DelegationScheduler>("reconcile", s => s.ReconcileAsync(stoppingToken), stoppingToken);
                lastReconcile = now;
            }

            if (lastRetention is null || now - lastRetention >= TimeSpan.FromDays(1))
            {
                await RunSafeAsync<RetentionService>("retention", s => s.RunAsync(stoppingToken), stoppingToken);
                lastRetention = now;
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RunSafeAsync<T>(string name, Func<T, Task> run, CancellationToken ct) where T : notnull
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await run(scope.ServiceProvider.GetRequiredService<T>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scheduler job '{Job}' failed", name);
        }
    }
}
