using MDel.Application.Abstractions;
using MDel.Application.Common;
using MDel.Domain.Enums;
using MDel.Infrastructure.Delegations;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Notifications;
using MDel.Infrastructure.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MDel.Api.Controllers;

/// <summary>Ecranul de administrare: setări (AD, Exchange, SMTP, fișiere, retenție…) și teste de conexiune.</summary>
[ApiController]
[Authorize(Policy = AppRoles.Admin)]
[Route("api/admin")]
public sealed class AdminController(
    ISettingsService settings,
    SettingsValidator validator,
    IDirectoryService directory,
    IExchangeService exchange,
    IMailService mail,
    AttachmentStorageProbe storage,
    ICurrentUser currentUser,
    IAuditService audit,
    InformGroupService informGroup) : ControllerBase
{
    public sealed record SettingsResponse(IReadOnlyList<SettingView> Settings, int MaxAttachmentMb);

    public sealed record UpdateSettingsRequest(Dictionary<string, string?> Values);

    public sealed record SmtpTestRequest(string? SendTo);

    [HttpGet("settings")]
    public async Task<SettingsResponse> GetSettings(CancellationToken ct) =>
        new(await settings.GetAllAsync(ct), validator.MaxAttachmentMb);

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(UpdateSettingsRequest request, CancellationToken ct)
    {
        if (request.Values is not { Count: > 0 }) return ValidationProblem(detail: "No values were provided.");

        try
        {
            await settings.UpdateAsync(request.Values, ct);
            return NoContent();
        }
        catch (SettingsValidationException ex)
        {
            foreach (var (key, messages) in ex.Errors)
                foreach (var message in messages)
                    ModelState.AddModelError(key, message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpPost("test/ad")]
    public Task<ConnectionTestResult> TestAd(CancellationToken ct) =>
        RunTest("ActiveDirectory", () => directory.TestConnectionAsync(ct), ct);

    [HttpPost("test/exchange")]
    public Task<ConnectionTestResult> TestExchange(CancellationToken ct) =>
        RunTest("Exchange", () => exchange.TestConnectionAsync(ct), ct);

    [HttpPost("test/storage")]
    public Task<ConnectionTestResult> TestStorage(CancellationToken ct) =>
        RunTest("Storage", () => storage.TestAsync(ct), ct);

    /// <summary>Testează conexiunea SMTP; opțional trimite un mail de test.</summary>
    [HttpPost("test/smtp")]
    public Task<ConnectionTestResult> TestSmtp(SmtpTestRequest? request, CancellationToken ct) =>
        RunTest("Smtp", async () =>
        {
            var result = await mail.TestConnectionAsync(ct);
            if (!result.Success || string.IsNullOrWhiteSpace(request?.SendTo)) return result;

            try
            {
                await mail.SendAsync(new MailMessage(
                    [request.SendTo.Trim()],
                    "MDel – test SMTP",
                    $"<p>Mesaj de test trimis din MDel de <b>{System.Net.WebUtility.HtmlEncode(currentUser.UserName)}</b>.</p>" +
                    "<p>Test message sent from MDel.</p>"), ct);
                return result with { Message = $"{result.Message} Test mail sent to {request.SendTo}." };
            }
            catch (Exception ex)
            {
                return new ConnectionTestResult(false, $"Connected, but sending failed: {ex.Message}");
            }
        }, ct);

    // ------------------------------------------------------------------ grup de informare

    public sealed record AddInformMemberRequest(string SamAccountName);

    [HttpGet("inform-group")]
    public Task<IReadOnlyList<InformGroupMemberDto>> InformGroup(CancellationToken ct) => informGroup.ListAsync(ct);

    [HttpPost("inform-group")]
    public async Task<IActionResult> AddInformMember(AddInformMemberRequest request, CancellationToken ct)
    {
        try
        {
            await informGroup.AddAsync(request.SamAccountName ?? string.Empty, ct);
            return Ok(await informGroup.ListAsync(ct));
        }
        catch (DelegationValidationException ex)
        {
            foreach (var (key, messages) in ex.Errors)
                foreach (var message in messages)
                    ModelState.AddModelError(key, message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpDelete("inform-group/{memberId:int}")]
    public async Task<IActionResult> RemoveInformMember(int memberId, CancellationToken ct) =>
        await informGroup.RemoveAsync(memberId, ct) ? NoContent() : NotFound();

    // ------------------------------------------------------------------ șabloane email (română)

    public sealed record TemplateMeta(TemplateKind Kind, string SubjectKey, string BodyKey, string DefaultSubject, string DefaultBody);

    public sealed record TemplatesMeta(IReadOnlyList<TemplateMeta> Templates, IReadOnlyList<string> Placeholders);

    public sealed record TemplatePreviewRequest(TemplateKind Kind, string? Subject, string? Body);

    public sealed record TemplatePreview(string Subject, string Html);

    [HttpGet("templates")]
    public TemplatesMeta Templates() => new(
        Enum.GetValues<TemplateKind>().Select(k => new TemplateMeta(k,
            NotificationTemplates.SubjectKey(k), NotificationTemplates.BodyKey(k),
            NotificationTemplates.RomanianDefaults[k].Subject, NotificationTemplates.RomanianDefaults[k].Body)).ToList(),
        NotificationTemplates.Placeholders);

    /// <summary>Previzualizare cu o delegare de exemplu (nu salvează nimic).</summary>
    [HttpPost("templates/preview")]
    public async Task<ActionResult<TemplatePreview>> PreviewTemplate(TemplatePreviewRequest request, CancellationToken ct)
    {
        var defaults = NotificationTemplates.RomanianDefaults[request.Kind];
        var subject = string.IsNullOrWhiteSpace(request.Subject) ? defaults.Subject : request.Subject;
        var body = string.IsNullOrWhiteSpace(request.Body) ? defaults.Body : request.Body;

        var errors = new[] { (Key: "subject", Text: subject), (Key: "body", Text: body) }
            .Select(x => (x.Key, Unknown: NotificationTemplates.UnknownPlaceholders(x.Text)))
            .Where(x => x.Unknown.Count > 0)
            .ToList();
        foreach (var (key, unknown) in errors)
            ModelState.AddModelError(key, $"Unknown fields: {string.Join(", ", unknown.Select(u => "{" + u + "}"))}");
        if (errors.Count > 0) return ValidationProblem(ModelState);

        var sample = SampleDelegation(request.Kind);
        var baseUrl = await settings.GetAsync(Domain.SettingKeys.AppBaseUrl, ct);
        var reason = request.Kind is TemplateKind.Rejected or TemplateKind.Stopped or TemplateKind.Failed
            ? "Revenire anticipată din concediu" : null;
        var (renderedSubject, html) = NotificationTemplates.Render(sample, request.Kind, "ro", reason,
            string.IsNullOrWhiteSpace(baseUrl) ? "https://mdel.contoso.local" : baseUrl,
            new NotificationTemplates.Template(subject, body));
        return new TemplatePreview(renderedSubject, html);
    }

    private static Domain.Entities.Delegation SampleDelegation(TemplateKind kind)
    {
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(7);
        return new Domain.Entities.Delegation
        {
            Id = 1024,
            FromUser = new Domain.Entities.AppUser { DisplayName = "Ion Popescu", Email = "ion.popescu@contoso.local", SamAccountName = "ion.popescu" },
            ToUser = new Domain.Entities.AppUser { DisplayName = "Maria Ionescu", Email = "maria.ionescu@contoso.local", SamAccountName = "maria.ionescu" },
            CreatedBy = new Domain.Entities.AppUser { DisplayName = "Mihai Stan" },
            ApprovedBy = kind is TemplateKind.Request or TemplateKind.Pending or TemplateKind.Direct or TemplateKind.Rejected
                ? null : new Domain.Entities.AppUser { DisplayName = "Andrei Dumitrescu" },
            StartUtc = start,
            EndUtc = start.AddDays(7).AddHours(8),
            RequiresApproval = kind != TemplateKind.Direct,
            Notes = "Concediu de odihnă",
        };
    }

    // ------------------------------------------------------------------ coada de emailuri

    [HttpGet("notifications")]
    public Task<IReadOnlyList<OutboxItem>> Notifications([FromServices] NotificationAdminService outbox,
        [FromQuery] OutboxFilter filter = OutboxFilter.Failed, CancellationToken ct = default) =>
        outbox.ListAsync(filter, ct);

    [HttpGet("notifications/failed-count")]
    public async Task<int> FailedNotifications([FromServices] NotificationAdminService outbox, CancellationToken ct) =>
        await outbox.CountFailedAsync(ct);

    [HttpPost("notifications/{id:long}/retry")]
    public async Task<IActionResult> RetryNotification(long id, [FromServices] NotificationAdminService outbox, CancellationToken ct) =>
        await outbox.RetryAsync(id, settings, ct) ? NoContent() : NotFound();

    /// <summary>Verifică forwardul curent al unei căsuțe (doar citire) – util pentru diagnostic.</summary>
    [HttpGet("exchange/forwarding")]
    public async Task<ActionResult<MailboxForwarding>> GetForwarding([FromQuery] string mailbox, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mailbox)) return ValidationProblem(detail: "mailbox is required.");
        try
        {
            return await exchange.GetForwardingAsync(mailbox.Trim(), ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExchangeCommandException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private async Task<ConnectionTestResult> RunTest(string target, Func<Task<ConnectionTestResult>> test, CancellationToken ct)
    {
        ConnectionTestResult result;
        try
        {
            result = await test();
        }
        catch (Exception ex)
        {
            result = new ConnectionTestResult(false, ex.Message);
        }

        await audit.WriteAsync(new AuditEntry($"Admin.Test.{target}", Details: result.Message,
            Result: result.Success ? AuditResult.Success : AuditResult.Failure), ct);
        return result;
    }
}
