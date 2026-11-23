using MDel.Application.Common;
using MDel.Domain.Enums;
using MDel.Infrastructure.Delegations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MDel.Api.Controllers;

[ApiController]
[Authorize(Policy = AppPolicies.Viewer)]
[Route("api/delegations")]
public sealed class DelegationsController(DelegationService delegations, AttachmentService attachments) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<DelegationListItem>> List(
        [FromQuery] DelegationStatus[]? status, [FromQuery] string? search, [FromQuery] bool? keepCopy,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        delegations.ListAsync(new DelegationQuery(status, search, keepCopy, page, pageSize), ct);

    [HttpGet("summary")]
    public Task<IReadOnlyDictionary<DelegationStatus, int>> Summary(CancellationToken ct) => delegations.SummaryAsync(ct);

    [HttpGet("stats")]
    public Task<DelegationStats> Stats(CancellationToken ct) => delegations.StatsAsync(ct);

    [HttpPost("{id:int}/approve")]
    [Authorize(Policy = AppRoles.Approver)]
    public Task<IActionResult> Approve(int id, DecisionRequest? request, CancellationToken ct) => Handle(async () =>
    {
        await delegations.ApproveAsync(id, request?.RowVersion, ct);
        return Ok(await delegations.GetAsync(id, ct));
    });

    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = AppRoles.Approver)]
    public Task<IActionResult> Reject(int id, DecisionRequest request, CancellationToken ct) => Handle(async () =>
    {
        await delegations.RejectAsync(id, request.Reason ?? string.Empty, request.RowVersion, ct);
        return Ok(await delegations.GetAsync(id, ct));
    });

    [HttpGet("form-config")]
    [Authorize(Policy = AppRoles.Admin)]
    public Task<DelegationFormConfig> FormConfig(CancellationToken ct) => delegations.GetFormConfigAsync(ct);

    [HttpGet("source-check")]
    [Authorize(Policy = AppRoles.Admin)]
    public Task<SourceCheckResult> SourceCheck([FromQuery] string fromSam, CancellationToken ct) =>
        delegations.CheckSourceAsync(fromSam, ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DelegationDetail>> Get(int id, CancellationToken ct) =>
        await delegations.GetAsync(id, ct) is { } d ? d : NotFound();

    [HttpPost]
    [Authorize(Policy = AppRoles.Admin)]
    public Task<IActionResult> Create(SaveDelegationRequest request, CancellationToken ct) => Handle(async () =>
    {
        var id = await delegations.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id }, await delegations.GetAsync(id, ct));
    });

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppRoles.Admin)]
    public Task<IActionResult> Update(int id, SaveDelegationRequest request, CancellationToken ct) => Handle(async () =>
    {
        await delegations.UpdateAsync(id, request, ct);
        return Ok(await delegations.GetAsync(id, ct));
    });

    [HttpPost("{id:int}/stop")]
    [Authorize(Policy = AppRoles.Admin)]
    public Task<IActionResult> Stop(int id, StopDelegationRequest request, CancellationToken ct) => Handle(async () =>
    {
        var ok = await delegations.StopAsync(id, request, ct);
        var detail = await delegations.GetAsync(id, ct);
        // Oprirea s-a înregistrat, dar Exchange a eșuat: 502 cu detaliul delegării (status Failed + LastError).
        return ok ? Ok(detail) : StatusCode(StatusCodes.Status502BadGateway, detail);
    });

    // ------------------------------------------------------------------ atașamente

    [HttpPost("{id:int}/attachments")]
    [Authorize(Policy = AppRoles.Admin)]
    // Limita per cerere: Uploads:MaxRequestBodyBytes (Kestrel/IIS); limita per fișier: setarea din administrare.
    // Frontend-ul trimite câte un fișier pe cerere.
    public Task<IActionResult> Upload(int id, [FromForm] List<IFormFile> files, CancellationToken ct) => Handle(async () =>
    {
        var uploads = files.Select(f => new UploadFile(f.FileName, f.Length, f.OpenReadStream)).ToList();
        await attachments.UploadAsync(id, uploads, ct);
        return Ok(await delegations.GetAsync(id, ct));
    });

    [HttpGet("{id:int}/attachments/{attachmentId:int}")]
    public Task<IActionResult> Download(int id, int attachmentId, CancellationToken ct) => Handle(async () =>
    {
        var file = await attachments.OpenAsync(id, attachmentId, ct);
        if (file is null) return NotFound();
        // Întotdeauna ca descărcare (Content-Disposition: attachment), niciodată afișat inline.
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
    });

    [HttpDelete("{id:int}/attachments/{attachmentId:int}")]
    [Authorize(Policy = AppRoles.Admin)]
    public Task<IActionResult> DeleteAttachment(int id, int attachmentId, CancellationToken ct) => Handle(async () =>
        await attachments.DeleteAsync(id, attachmentId, ct) ? NoContent() : NotFound());

    private async Task<IActionResult> Handle(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DelegationValidationException ex)
        {
            foreach (var (key, messages) in ex.Errors)
                foreach (var message in messages)
                    ModelState.AddModelError(ToCamel(key), message);
            return ValidationProblem(ModelState);
        }
        catch (DelegationConflictException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private static string ToCamel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
