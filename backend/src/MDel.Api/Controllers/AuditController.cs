using MDel.Application.Abstractions;
using MDel.Application.Common;
using MDel.Domain.Enums;
using MDel.Infrastructure.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MDel.Api.Controllers;

/// <summary>Ecranul „Istoric / Audit”: toate acțiunile utilizatorilor și ale sistemului.</summary>
[ApiController]
[Authorize(Policy = AppRoles.SystemAdmin)]
[Route("api/audit")]
public sealed class AuditController(AuditQueryService audit) : ControllerBase
{
    public sealed class AuditParameters
    {
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public string? User { get; init; }
        public string? Action { get; init; }
        public string? EntityType { get; init; }
        public string? EntityId { get; init; }
        public AuditResult? Result { get; init; }
        public string? Search { get; init; }
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 50;

        public AuditQuery ToQuery() => new(From, To, User, Action, EntityType, EntityId, Result, Search, Page, PageSize);
    }

    [HttpGet]
    public Task<AuditPage> List([FromQuery] AuditParameters p, CancellationToken ct) => audit.ListAsync(p.ToQuery(), ct);

    [HttpGet("actions")]
    public Task<IReadOnlyList<string>> Actions(CancellationToken ct) => audit.ActionsAsync(ct);

    [HttpGet("{id:long}")]
    public async Task<ActionResult<AuditDetail>> Get(long id, CancellationToken ct) =>
        await audit.GetAsync(id, ct) is { } entry ? entry : NotFound();

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] AuditParameters p, [FromServices] IAuditService writer, CancellationToken ct)
    {
        var csv = await audit.ExportAsync(p.ToQuery(), ct);
        // Și exportul auditului e o acțiune auditată.
        await writer.WriteAsync(new AuditEntry("Audit.Exported", Details: $"filters: action={p.Action}, user={p.User}, from={p.From:O}, to={p.To:O}"), ct);
        return File(csv, "text/csv; charset=utf-8", $"mdel-audit-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }
}
