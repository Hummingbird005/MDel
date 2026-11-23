using System.Text.Json;
using MDel.Application.Abstractions;
using MDel.Domain.Entities;
using MDel.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MDel.Infrastructure.Auditing;

/// <summary>
/// Scrie auditul printr-un DbContext separat, ca o eroare de salvare din cererea curentă
/// (entități rămase în change tracker) să nu împiedice sau să polueze înregistrarea de audit.
/// </summary>
public sealed class AuditService(IServiceScopeFactory scopeFactory, ICurrentUser currentUser, ILogger<AuditService> logger)
    : IAuditService
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            UserId = currentUser.UserId,
            UserName = Truncate(currentUser.UserName, 256)!,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            OldValues = Serialize(entry.OldValues),
            NewValues = Serialize(entry.NewValues),
            Details = Truncate(entry.Details, 4000),
            HttpMethod = entry.HttpMethod,
            Path = Truncate(entry.Path, 2048),
            StatusCode = entry.StatusCode,
            IpAddress = currentUser.IpAddress,
            CorrelationId = currentUser.CorrelationId,
            Result = entry.Result,
        };

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MDelDbContext>();
            db.AuditLogs.Add(log);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Auditul nu trebuie să blocheze operația; eșecul se loghează.
            logger.LogError(ex, "Failed to write audit entry {Action} for {User}", entry.Action, log.UserName);
        }
    }

    private static string? Serialize(object? value) =>
        value is null ? null : value as string ?? JsonSerializer.Serialize(value);

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
