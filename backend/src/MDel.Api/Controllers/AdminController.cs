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
