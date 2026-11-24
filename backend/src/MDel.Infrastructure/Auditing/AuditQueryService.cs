using System.Globalization;
using System.Text;
using MDel.Domain.Enums;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MDel.Infrastructure.Auditing;

public sealed record AuditQuery(
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? User = null,
    string? Action = null,
    string? EntityType = null,
    string? EntityId = null,
    AuditResult? Result = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50,
    bool IncludeHttp = false);

public sealed record AuditListItem(
    long Id,
    DateTime TimestampUtc,
    string UserName,
    string? UserDisplayName,
    string Action,
    string? EntityType,
    string? EntityId,
    string? Details,
    string? HttpMethod,
    string? Path,
    int? StatusCode,
    string? IpAddress,
    AuditResult Result,
    bool HasChanges,
    string? OldValues,
    string? NewValues);

public sealed record AuditDetail(
    long Id,
    DateTime TimestampUtc,
    string UserName,
    string? UserDisplayName,
    string Action,
    string? EntityType,
    string? EntityId,
    string? Details,
    string? HttpMethod,
    string? Path,
    int? StatusCode,
    string? IpAddress,
    string? CorrelationId,
    AuditResult Result,
    string? OldValues,
    string? NewValues);

public sealed record AuditPage(IReadOnlyList<AuditListItem> Items, int Total, int Page, int PageSize);

/// <summary>Citirea jurnalului de audit pentru ecranul „Istoric / Audit” (doar administratori).</summary>
public sealed class AuditQueryService(MDelDbContext db)
{
    public async Task<AuditPage> ListAsync(AuditQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var q = Filter(query);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditListItem(a.Id, a.TimestampUtc, a.UserName, a.User != null ? a.User.DisplayName : null,
                a.Action, a.EntityType, a.EntityId, a.Details, a.HttpMethod, a.Path, a.StatusCode, a.IpAddress, a.Result,
                a.OldValues != null || a.NewValues != null, a.OldValues, a.NewValues))
            .ToListAsync(ct);
        return new AuditPage(items, total, page, pageSize);
    }

    public Task<AuditDetail?> GetAsync(long id, CancellationToken ct) =>
        db.AuditLogs.AsNoTracking().Where(a => a.Id == id)
            .Select(a => new AuditDetail(a.Id, a.TimestampUtc, a.UserName, a.User != null ? a.User.DisplayName : null,
                a.Action, a.EntityType, a.EntityId, a.Details, a.HttpMethod, a.Path, a.StatusCode, a.IpAddress,
                a.CorrelationId, a.Result, a.OldValues, a.NewValues))
            .FirstOrDefaultAsync(ct);

    /// <summary>Acțiunile existente în jurnal, pentru filtrul din interfață.</summary>
    public async Task<IReadOnlyList<string>> ActionsAsync(CancellationToken ct) =>
        await db.AuditLogs.AsNoTracking().Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);

    /// <summary>Export CSV (max. 50 000 rânduri, cele mai recente).</summary>
    public async Task<byte[]> ExportAsync(AuditQuery query, CancellationToken ct)
    {
        var rows = await Filter(query).OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id).Take(50_000)
            .Select(a => new { a.Id, a.TimestampUtc, a.UserName, a.Action, a.EntityType, a.EntityId, a.Result, a.HttpMethod,
                a.Path, a.StatusCode, a.IpAddress, a.Details, a.OldValues, a.NewValues })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Id;TimestampUtc;User;Action;EntityType;EntityId;Result;Method;Path;Status;IP;Details;OldValues;NewValues");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(';',
                r.Id.ToString(CultureInfo.InvariantCulture),
                r.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Csv(r.UserName), Csv(r.Action), Csv(r.EntityType), Csv(r.EntityId), r.Result.ToString(),
                Csv(r.HttpMethod), Csv(r.Path), r.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "",
                Csv(r.IpAddress), Csv(r.Details), Csv(r.OldValues), Csv(r.NewValues)));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private IQueryable<Domain.Entities.AuditLog> Filter(AuditQuery query)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (query.FromUtc is { } from)
        {
            var f = DateTime.SpecifyKind(from.ToUniversalTime(), DateTimeKind.Utc);
            q = q.Where(a => a.TimestampUtc >= f);
        }

        if (query.ToUtc is { } to)
        {
            var t = DateTime.SpecifyKind(to.ToUniversalTime(), DateTimeKind.Utc);
            q = q.Where(a => a.TimestampUtc < t);
        }

        if (!string.IsNullOrWhiteSpace(query.User))
        {
            var u = query.User.Trim();
            q = q.Where(a => a.UserName.Contains(u) || (a.User != null && a.User.DisplayName.Contains(u)));
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            // „Delegation.” = toate acțiunile pe delegări; altfel potrivire exactă.
            var action = query.Action.Trim();
            q = action.EndsWith('.') ? q.Where(a => a.Action.StartsWith(action)) : q.Where(a => a.Action == action);
        }

        // Apelurile HTTP reușite dublează acțiunea de business (care are valorile): se ascund implicit.
        // Cele refuzate (401/403) sau eșuate rămân vizibile; toate se văd cu IncludeHttp sau când se filtrează „Http.”.
        var httpRequested = query.IncludeHttp || (query.Action?.Trim().StartsWith("Http", StringComparison.Ordinal) ?? false);
        if (!httpRequested) q = q.Where(a => !(a.Action == "Http.Request" && a.Result == AuditResult.Success));

        if (!string.IsNullOrWhiteSpace(query.EntityType)) q = q.Where(a => a.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityId)) q = q.Where(a => a.EntityId == query.EntityId);
        if (query.Result is { } result) q = q.Where(a => a.Result == result);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            // Se caută și în valorile înregistrate (ex. un nume de utilizator sau o dată introdusă într-un câmp).
            q = q.Where(a => (a.Details != null && a.Details.Contains(s)) || (a.Path != null && a.Path.Contains(s)) ||
                             (a.IpAddress != null && a.IpAddress.Contains(s)) || (a.EntityId != null && a.EntityId == s) ||
                             (a.NewValues != null && a.NewValues.Contains(s)) || (a.OldValues != null && a.OldValues.Contains(s)));
        }

        return q;
    }

    /// <summary>Ghilimele la nevoie; neutralizează formulele (CSV injection).</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if ("=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
