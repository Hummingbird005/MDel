using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Tests;

/// <summary>Fiecare test primește o aplicație nouă (bază in-memory și Exchange fake proprii).</summary>
public sealed class DelegationApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly MDelApiFactory _factory = new();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "mdel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _factory.Dispose();
        try { Directory.Delete(_storage, recursive: true); } catch { /* ignore */ }
    }

    private sealed record UserRefDto(int Id, string SamAccountName, string DisplayName, string? Email);
    private sealed record AttachmentDto(int Id, string FileName, long SizeBytes, bool Purged);
    private sealed record HistoryDto(string? FromStatus, string ToStatus, string? ChangedBy, string? Reason);
    private sealed record ActionsDto(bool CanEdit, bool CanEditEndOnly, bool CanStop, bool CanManageAttachments);
    private sealed record DetailDto(
        int Id, string Status, UserRefDto From, UserRefDto To, DateTime StartUtc, DateTime EndUtc,
        bool RequiresApproval, bool KeepCopyInSource, string? Notes, UserRefDto CreatedBy, UserRefDto? StoppedBy,
        string? StopReason, string? PreviousForwardingAddress, string? LastError, string RowVersion,
        AttachmentDto[] Attachments, HistoryDto[] History, ActionsDto Actions);
    private sealed record ListDto(DetailDto[] Items, int Total);

    private HttpClient Admin() => _factory.CreateClient();

    private HttpClient ApproverOnly()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\dev.approver");
        c.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Approvers");
        return c;
    }

    private static object Request(string from, string to, DateTime start, DateTime end, bool approval = true, string? notes = null,
        string? rowVersion = null) =>
        new { fromSam = from, toSam = to, startUtc = start, endUtc = end, requiresApproval = approval, notes, rowVersion };

    private static DateTime Now => DateTime.UtcNow;

    private async Task<DetailDto> CreateOk(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/delegations", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DetailDto>(Json))!;
    }

    private static async Task<JsonElement> Errors(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
    }

    private FakeExchangeService Exchange => (FakeExchangeService)_factory.Services.GetRequiredService<IExchangeService>();

    // ------------------------------------------------------------------ creare

    [Fact]
    public async Task Create_with_approval_is_pending_with_history_and_creator()
    {
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(3), notes: "Concediu"));

        Assert.Equal("PendingApproval", d.Status);
        Assert.Equal("dev.admin", d.CreatedBy.SamAccountName);
        Assert.Equal("Ion Popescu", d.From.DisplayName);
        Assert.Equal("Concediu", d.Notes);
        Assert.True(d.KeepCopyInSource);
        Assert.Equal("PendingApproval", Assert.Single(d.History).ToStatus);
        Assert.Null(Exchange.GetForwardingAsync("ion.popescu@contoso.local", default).Result.ForwardingAddress);

        Assert.True(_factory.WithDb(db => db.AuditLogs.Any(a => a.Action == "Delegation.Created" && a.EntityId == d.Id.ToString())));
    }

    [Fact]
    public async Task Create_without_approval_in_the_future_is_scheduled()
    {
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddHours(2), Now.AddDays(1), approval: false));
        Assert.Equal("Scheduled", d.Status);
        Assert.False(d.RequiresApproval);
    }

    [Fact]
    public async Task Create_without_approval_starting_now_activates_forwarding_and_keeps_previous()
    {
        Exchange.Seed("ion.popescu@contoso.local", new MailboxForwarding("contoso.local/Users/Old Target", false));

        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now, Now.AddDays(1), approval: false));

        Assert.Equal("Active", d.Status);
        Assert.Equal("contoso.local/Users/Old Target", d.PreviousForwardingAddress);
        var forward = await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default);
        Assert.Equal("maria.ionescu@contoso.local", forward.ForwardingAddress);
        Assert.True(forward.DeliverToMailboxAndForward);
        Assert.Equal(["Scheduled", "Active"], d.History.Select(h => h.ToStatus));
    }

    [Fact]
    public async Task Keep_copy_follows_the_global_setting()
    {
        await Admin().PutAsJsonAsync("/api/admin/settings",
            new { values = new Dictionary<string, string> { [SettingKeys.ExchangeKeepCopyInSource] = "false" } });

        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now, Now.AddDays(1), approval: false));

        Assert.False(d.KeepCopyInSource);
        Assert.False((await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default)).DeliverToMailboxAndForward);
    }

    // ------------------------------------------------------------------ validare

    [Fact]
    public async Task Validation_errors_are_reported_per_field()
    {
        var response = await Admin().PostAsJsonAsync("/api/delegations",
            Request("ion.popescu", "ion.popescu", Now.AddDays(-2), Now.AddDays(-3)));
        var errors = await Errors(response);

        Assert.True(errors.TryGetProperty("toSam", out _));
        Assert.True(errors.TryGetProperty("endUtc", out _));
        Assert.True(errors.TryGetProperty("startUtc", out _));
    }

    [Theory]
    [InlineData("svc.nomailbox", "fromSam")]
    [InlineData("nobody.here", "fromSam")]
    [InlineData("", "fromSam")]
    public async Task Source_must_exist_and_have_a_mailbox(string from, string field)
    {
        var errors = await Errors(await Admin().PostAsJsonAsync("/api/delegations",
            Request(from, "maria.ionescu", Now.AddHours(1), Now.AddDays(1))));
        Assert.True(errors.TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Errors_are_localized_by_accept_language()
    {
        var client = Admin();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
        var en = await Errors(await client.PostAsJsonAsync("/api/delegations",
            Request("ion.popescu", "ion.popescu", Now.AddHours(1), Now.AddDays(1))));
        Assert.Contains("must differ", en.GetProperty("toSam")[0].GetString());

        var ro = await Errors(await Admin().PostAsJsonAsync("/api/delegations",
            Request("ion.popescu", "ion.popescu", Now.AddHours(1), Now.AddDays(1))));
        Assert.Contains("diferit", ro.GetProperty("toSam")[0].GetString());
    }

    [Fact]
    public async Task Overlapping_delegation_on_the_same_source_is_rejected()
    {
        var first = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(5)));

        var errors = await Errors(await Admin().PostAsJsonAsync("/api/delegations",
            Request("ion.popescu", "dev.approver", Now.AddDays(4), Now.AddDays(8))));
        Assert.Contains($"#{first.Id}", errors.GetProperty("fromSam")[0].GetString());

        // Imediat după (fără suprapunere) este permis.
        await CreateOk(Admin(), Request("ion.popescu", "dev.approver", Now.AddDays(5), Now.AddDays(8)));
    }

    [Fact]
    public async Task Reverse_forward_in_the_same_interval_is_rejected_as_a_loop()
    {
        await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(5)));

        var errors = await Errors(await Admin().PostAsJsonAsync("/api/delegations",
            Request("maria.ionescu", "ion.popescu", Now.AddDays(2), Now.AddDays(3))));
        Assert.True(errors.TryGetProperty("toSam", out _));
    }

    // ------------------------------------------------------------------ modificare

    [Fact]
    public async Task Pending_delegation_can_be_edited_and_is_audited()
    {
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(3)));

        var response = await Admin().PutAsJsonAsync($"/api/delegations/{d.Id}",
            Request("ion.popescu", "dev.approver", Now.AddDays(2), Now.AddDays(4), notes: "schimbat", rowVersion: d.RowVersion));
        var updated = await response.Content.ReadFromJsonAsync<DetailDto>(Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("dev.approver", updated!.To.SamAccountName);
        Assert.Equal("schimbat", updated.Notes);
        Assert.True(_factory.WithDb(db => db.AuditLogs.Any(a => a.Action == "Delegation.Updated" && a.OldValues!.Contains("maria.ionescu"))));
    }

    [Fact]
    public async Task Active_delegation_allows_only_end_date_changes()
    {
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now, Now.AddDays(1), approval: false));
        Assert.True(d.Actions.CanEditEndOnly);

        var changeTarget = await Admin().PutAsJsonAsync($"/api/delegations/{d.Id}",
            Request("ion.popescu", "dev.approver", d.StartUtc, d.EndUtc));
        Assert.Equal(HttpStatusCode.BadRequest, changeTarget.StatusCode);

        var extend = await Admin().PutAsJsonAsync($"/api/delegations/{d.Id}",
            Request("ion.popescu", "maria.ionescu", d.StartUtc, d.EndUtc.AddDays(2)));
        var updated = await extend.Content.ReadFromJsonAsync<DetailDto>(Json);
        Assert.Equal(HttpStatusCode.OK, extend.StatusCode);
        Assert.Equal(d.EndUtc.AddDays(2), updated!.EndUtc, TimeSpan.FromSeconds(1));
        Assert.Equal("Active", updated.Status);
    }

    [Fact]
    public async Task Stale_row_version_is_a_conflict()
    {
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(3)));
        var response = await Admin().PutAsJsonAsync($"/api/delegations/{d.Id}",
            Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(4), rowVersion: "AAAAAAAAB9E="));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ------------------------------------------------------------------ oprire

    [Fact]
    public async Task Stopping_an_active_delegation_restores_the_previous_forward()
    {
        Exchange.Seed("ion.popescu@contoso.local", new MailboxForwarding("contoso.local/Users/Old Target", false));
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now, Now.AddDays(1), approval: false));

        var response = await Admin().PostAsJsonAsync($"/api/delegations/{d.Id}/stop", new { reason = "Revenire anticipată" });
        var stopped = await response.Content.ReadFromJsonAsync<DetailDto>(Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Stopped", stopped!.Status);
        Assert.Equal("dev.admin", stopped.StoppedBy!.SamAccountName);
        Assert.Equal("Revenire anticipată", stopped.StopReason);
        Assert.False(stopped.Actions.CanStop);
        Assert.Equal("contoso.local/Users/Old Target",
            (await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default)).ForwardingAddress);
    }

    [Fact]
    public async Task Forward_changed_outside_the_app_is_not_overwritten_on_stop()
    {
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now, Now.AddDays(1), approval: false));
        Exchange.Seed("ion.popescu@contoso.local", new MailboxForwarding("someone.else@contoso.local", true));

        var stopped = await (await Admin().PostAsJsonAsync($"/api/delegations/{d.Id}/stop", new { reason = "x" }))
            .Content.ReadFromJsonAsync<DetailDto>(Json);

        Assert.Equal("Stopped", stopped!.Status);
        Assert.Contains("outside MDel", stopped.LastError);
        Assert.Equal("someone.else@contoso.local", (await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default)).ForwardingAddress);
    }

    [Fact]
    public async Task Stop_requires_a_reason_and_cannot_be_repeated()
    {
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(2)));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin().PostAsJsonAsync($"/api/delegations/{d.Id}/stop", new { reason = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Admin().PostAsJsonAsync($"/api/delegations/{d.Id}/stop", new { reason = "anulat" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Admin().PostAsJsonAsync($"/api/delegations/{d.Id}/stop", new { reason = "din nou" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin().PutAsJsonAsync($"/api/delegations/{d.Id}",
            Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(2)))).StatusCode);
    }

    // ------------------------------------------------------------------ listă și roluri

    [Fact]
    public async Task List_filters_by_status_and_search_and_summary_counts()
    {
        await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now, Now.AddDays(1), approval: false));
        await CreateOk(Admin(), Request("maria.ionescu", "dev.approver", Now.AddDays(2), Now.AddDays(3)));

        var active = await Admin().GetFromJsonAsync<ListDto>("/api/delegations?status=Active", Json);
        Assert.Equal(1, active!.Total);

        var search = await Admin().GetFromJsonAsync<ListDto>("/api/delegations?search=Maria", Json);
        Assert.Equal(2, search!.Total);

        var summary = await Admin().GetFromJsonAsync<Dictionary<string, int>>("/api/delegations/summary");
        Assert.Equal(1, summary!["Active"]);
        Assert.Equal(1, summary["PendingApproval"]);
    }

    [Fact]
    public async Task Approver_can_view_but_not_create_or_stop()
    {
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(2)));
        var approver = ApproverOnly();

        var detail = await approver.GetFromJsonAsync<DetailDto>($"/api/delegations/{d.Id}", Json);
        Assert.False(detail!.Actions.CanStop);
        Assert.False(detail.Actions.CanEdit);

        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PostAsJsonAsync("/api/delegations",
            Request("ion.popescu", "maria.ionescu", Now.AddDays(5), Now.AddDays(6)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await approver.PostAsJsonAsync($"/api/delegations/{d.Id}/stop", new { reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task User_without_roles_cannot_see_delegations()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\ion.popescu");
        c.DefaultRequestHeaders.Add("X-Dev-Groups", "none");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/delegations")).StatusCode);
    }

    [Fact]
    public async Task Source_check_reports_existing_forward_and_open_delegations()
    {
        Exchange.Seed("ion.popescu@contoso.local", new MailboxForwarding("contoso.local/Users/Old Target", true));
        await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(2)));

        var check = await Admin().GetFromJsonAsync<JsonElement>("/api/delegations/source-check?fromSam=ion.popescu");
        Assert.Equal("contoso.local/Users/Old Target", check.GetProperty("currentForwarding").GetProperty("forwardingAddress").GetString());
        Assert.Equal(1, check.GetProperty("openDelegations").GetArrayLength());
    }

    // ------------------------------------------------------------------ atașamente

    private async Task ConfigureStorage(int maxMb = 100, string extensions = ".pdf,.txt")
    {
        var response = await Admin().PutAsJsonAsync("/api/admin/settings", new
        {
            values = new Dictionary<string, string>
            {
                [SettingKeys.AttachmentsStoragePath] = _storage,
                [SettingKeys.AttachmentsMaxSizeMb] = maxMb.ToString(),
                [SettingKeys.AttachmentsAllowedExtensions] = extensions,
            },
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static MultipartFormDataContent Files(params (string Name, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, content) in files)
        {
            form.Add(new ByteArrayContent(content), "files", name);
        }

        return form;
    }

    [Fact]
    public async Task Attachments_can_be_uploaded_downloaded_and_deleted()
    {
        await ConfigureStorage();
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(2)));
        var content = "aprobare scrisă"u8.ToArray();

        var upload = await Admin().PostAsync($"/api/delegations/{d.Id}/attachments", Files(("..\\..\\aprobare.txt", content)));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var attachment = Assert.Single((await upload.Content.ReadFromJsonAsync<DetailDto>(Json))!.Attachments);
        Assert.Equal("aprobare.txt", attachment.FileName); // calea din numele trimis e eliminată

        var files = Directory.GetFiles(_storage, "*", SearchOption.AllDirectories);
        Assert.Single(files);
        Assert.EndsWith(".bin", files[0]);

        var download = await ApproverOnly().GetAsync($"/api/delegations/{d.Id}/attachments/{attachment.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Admin().DeleteAsync($"/api/delegations/{d.Id}/attachments/{attachment.Id}")).StatusCode);
        Assert.Empty(Directory.GetFiles(_storage, "*", SearchOption.AllDirectories));

        var actions = _factory.WithDb(db => db.AuditLogs.Where(a => a.Action.StartsWith("Attachment.")).Select(a => a.Action).ToList());
        Assert.Equal(["Attachment.Uploaded", "Attachment.Downloaded", "Attachment.Deleted"], actions);
    }

    [Fact]
    public async Task A_file_name_with_a_very_long_extension_is_refused_cleanly_not_with_a_server_error()
    {
        await ConfigureStorage();
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(2)));

        var response = await Admin().PostAsync($"/api/delegations/{d.Id}/attachments",
            Files(("raport." + new string('x', 250), [1, 2, 3])));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await Errors(response)).TryGetProperty("files", out _));
    }

    [Fact]
    public async Task Attachments_respect_extension_and_size_limits()
    {
        await ConfigureStorage(maxMb: 1);
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(2)));

        var wrongType = await Admin().PostAsync($"/api/delegations/{d.Id}/attachments", Files(("script.exe", [1, 2, 3])));
        Assert.True((await Errors(wrongType)).TryGetProperty("files", out _));

        var tooBig = await Admin().PostAsync($"/api/delegations/{d.Id}/attachments",
            Files(("mare.pdf", new byte[1024 * 1024 + 1])));
        Assert.True((await Errors(tooBig)).TryGetProperty("files", out _));

        Assert.False(Directory.Exists(_storage) && Directory.GetFiles(_storage, "*", SearchOption.AllDirectories).Length > 0);
    }

    [Fact]
    public async Task Upload_without_configured_storage_is_a_conflict()
    {
        var d = await CreateOk(Admin(), Request("ion.popescu", "maria.ionescu", Now.AddDays(1), Now.AddDays(2)));
        var response = await Admin().PostAsync($"/api/delegations/{d.Id}/attachments", Files(("a.pdf", [1])));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
