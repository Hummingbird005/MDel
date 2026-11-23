using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MDel.Infrastructure.Scheduling;

public sealed record RetentionResult(int AttachmentsPurged, int AuditEntriesPurged);

/// <summary>
/// Retenție (implicit 365 de zile, configurabilă separat în Setări → Retenție):
/// fișierele atașate mai vechi se șterg de pe disc (înregistrarea rămâne, marcată „purged”),
/// iar înregistrările de audit mai vechi se șterg. Rezultatul se auditează.
/// </summary>
public sealed class RetentionService(
    MDelDbContext db,
    ISettingsService settings,
    IAuditService audit,
    TimeProvider time,
    ILogger<RetentionService> logger)
{
    public async Task<RetentionResult> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var attachmentDays = await settings.GetIntAsync(SettingKeys.RetentionAttachmentsDays, 365, ct);
        var auditDays = await settings.GetIntAsync(SettingKeys.RetentionAuditDays, 365, ct);

        var attachments = await PurgeAttachmentsAsync(now.AddDays(-attachmentDays), ct);
        var auditCount = await db.PurgeAuditLogsAsync(now.AddDays(-auditDays), ct);

        if (attachments + auditCount > 0)
        {
            await audit.WriteAsync(new AuditEntry("Retention.Purged", Details:
                $"{attachments} attachment(s) older than {attachmentDays} days, {auditCount} audit entries older than {auditDays} days"), ct);
            logger.LogInformation("Retention: {Attachments} attachments and {Audit} audit entries purged", attachments, auditCount);
        }

        return new RetentionResult(attachments, auditCount);
    }

    private async Task<int> PurgeAttachmentsAsync(DateTime cutoffUtc, CancellationToken ct)
    {
        var root = await settings.GetAsync(SettingKeys.AttachmentsStoragePath, ct);
        var old = await db.DelegationAttachments
            .Where(a => a.PurgedUtc == null && a.UploadedUtc < cutoffUtc)
            .Take(500)
            .ToListAsync(ct);

        foreach (var a in old)
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                var full = Path.GetFullPath(Path.Combine(root, a.StoredFileName.Replace('/', Path.DirectorySeparatorChar)));
                if (full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(full);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(ex, "Could not delete attachment file {Path}; will retry next run", full);
                        continue;
                    }
                }
            }

            a.PurgedUtc = time.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(ct);
        return old.Count(a => a.PurgedUtc is not null);
    }
}
