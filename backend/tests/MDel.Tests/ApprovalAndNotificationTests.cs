using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Domain.Enums;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Tests;

public sealed class ApprovalAndNotificationTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly MDelApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record UserRefDto(string SamAccountName, string DisplayName);
    private sealed record NotificationDto(string RecipientEmail, string RecipientType, string Event, string? Error);
    private sealed record ActionsDto(bool CanApprove, bool CanReject, bool IsOwnRequest, bool CanStop);
    private sealed record DetailDto(int Id, string Status, UserRefDto? ApprovedBy, string? RejectReason, string RowVersion,
        NotificationDto[] Notifications, ActionsDto Actions);

    /// <summary>Admin (creează) – dev.admin, doar în grupul de administratori.</summary>
    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Admins,CONTOSO\\MDel-System");
        return c;
    }

    /// <summary>Admin + aprobator (configurația implicită din Development).</summary>
    private HttpClient AdminApprover() => _factory.CreateClient();

    private HttpClient Approver(string user = "dev.approver")
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-User", $"CONTOSO\\{user}");
        c.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Approvers");
        return c;
    }

    private FakeMailService Mail => (FakeMailService)_factory.Services.GetRequiredService<IMailService>();
    private FakeExchangeService Exchange => (FakeExchangeService)_factory.Services.GetRequiredService<IExchangeService>();
    private Task<int> Dispatch() => _factory.Services.GetRequiredService<NotificationDispatcher>().ProcessPendingAsync(default);

    private async Task ConfigureGroups()
    {
        var r = await AdminApprover().PutAsJsonAsync("/api/admin/settings", new
        {
            values = new Dictionary<string, string>
            {
                [SettingKeys.GroupApproversEmail] = "mdel-approvers@contoso.local",
                [SettingKeys.GroupAdminsEmail] = "mdel-admins@contoso.local",
                [SettingKeys.AppBaseUrl] = "https://mdel.contoso.local",
            },
        });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
    }

    private static object Request(DateTime start, DateTime end, bool approval = true, string from = "ion.popescu", string to = "maria.ionescu") =>
        new { fromSam = from, toSam = to, startUtc = start, endUtc = end, requiresApproval = approval };

    private async Task<DetailDto> Create(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/delegations", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DetailDto>(Json))!;
    }

    private async Task<DetailDto> Get(int id) => (await AdminApprover().GetFromJsonAsync<DetailDto>($"/api/delegations/{id}", Json))!;

    // ------------------------------------------------------------------ aprobare

    [Fact]
    public async Task Request_goes_to_approvers_and_one_approval_schedules_it()
    {
        await ConfigureGroups();
        var d = await Create(Admin(), Request(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(3)));

        var request = Assert.Single(d.Notifications);
        Assert.Equal("ApproverGroup", request.RecipientType);
        Assert.Equal("mdel-approvers@contoso.local", request.RecipientEmail);

        await Dispatch();
        var mail = Assert.Single(Mail.Sent, m => m.To.Contains("mdel-approvers@contoso.local"));
        Assert.StartsWith("[MDel] Cerere de aprobare", mail.Subject);
        Assert.Contains("https://mdel.contoso.local/delegations/" + d.Id, mail.HtmlBody);

        var approver = Approver();
        var view = await approver.GetFromJsonAsync<DetailDto>($"/api/delegations/{d.Id}", Json);
        Assert.True(view!.Actions.CanApprove);

        var response = await approver.PostAsJsonAsync($"/api/delegations/{d.Id}/approve", new { rowVersion = view.RowVersion });
        var approved = await response.Content.ReadFromJsonAsync<DetailDto>(Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Scheduled", approved!.Status);
        Assert.Equal("dev.approver", approved.ApprovedBy!.SamAccountName);
        Assert.Contains(approved.Notifications, n => n.Event == "Approved" && n.RecipientType == "SourceUser");
        Assert.Contains(approved.Notifications, n => n.Event == "Approved" && n.RecipientType == "TargetUser");
        Assert.Contains(approved.Notifications, n => n.Event == "Approved" && n.RecipientType == "AdminGroup");
        Assert.True(_factory.WithDb(db => db.AuditLogs.Any(a => a.Action == "Delegation.Approved")));

        // Al doilea aprobator: cererea e deja procesată.
        var second = await Approver("dev.admin").PostAsJsonAsync($"/api/delegations/{d.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Approving_when_the_start_has_passed_activates_the_forward()
    {
        var d = await Create(Admin(), Request(DateTime.UtcNow, DateTime.UtcNow.AddDays(2)));
        var response = await Approver().PostAsJsonAsync($"/api/delegations/{d.Id}/approve", new { });
        var approved = await response.Content.ReadFromJsonAsync<DetailDto>(Json);

        Assert.Equal("Active", approved!.Status);
        Assert.Equal("maria.ionescu@contoso.local", (await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default)).ForwardingAddress);
    }

    [Fact]
    public async Task Creator_cannot_approve_own_request_unless_allowed_in_settings()
    {
        var d = await Create(AdminApprover(), Request(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2)));
        Assert.True(d.Actions.IsOwnRequest);

        var own = await AdminApprover().PostAsJsonAsync($"/api/delegations/{d.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Contains("propria cerere", (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        await AdminApprover().PutAsJsonAsync("/api/admin/settings",
            new { values = new Dictionary<string, string> { [SettingKeys.PreventSelfApproval] = "false" } });
        Assert.Equal(HttpStatusCode.OK, (await AdminApprover().PostAsJsonAsync($"/api/delegations/{d.Id}/approve", new { })).StatusCode);
    }

    [Fact]
    public async Task Admin_without_approver_role_cannot_approve()
    {
        var d = await Create(AdminApprover(), Request(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2)));
        var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\maria.ionescu");
        admin.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Admins,CONTOSO\\MDel-System");

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/delegations/{d.Id}/approve", new { })).StatusCode);
        Assert.False((await admin.GetFromJsonAsync<DetailDto>($"/api/delegations/{d.Id}", Json))!.Actions.CanApprove);
    }

    [Fact]
    public async Task Reject_requires_a_reason_and_notifies_admins()
    {
        await ConfigureGroups();
        var d = await Create(Admin(), Request(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2)));

        Assert.Equal(HttpStatusCode.BadRequest, (await Approver().PostAsJsonAsync($"/api/delegations/{d.Id}/reject", new { reason = "" })).StatusCode);

        var response = await Approver().PostAsJsonAsync($"/api/delegations/{d.Id}/reject", new { reason = "Lipsește aprobarea șefului direct" });
        var rejected = await response.Content.ReadFromJsonAsync<DetailDto>(Json);

        Assert.Equal("Rejected", rejected!.Status);
        Assert.Equal("Lipsește aprobarea șefului direct", rejected.RejectReason);
        var n = Assert.Single(rejected.Notifications, x => x.Event == "Rejected");
        Assert.Equal("mdel-admins@contoso.local", n.RecipientEmail);
        Assert.Equal(HttpStatusCode.Conflict, (await Approver().PostAsJsonAsync($"/api/delegations/{d.Id}/approve", new { })).StatusCode);
    }

    // ------------------------------------------------------------------ notificări

    [Fact]
    public async Task Without_approval_everyone_is_informed_and_nobody_is_asked_to_approve()
    {
        await ConfigureGroups();
        var d = await Create(Admin(), Request(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2), approval: false));

        Assert.Equal("Scheduled", d.Status);
        Assert.Equal(["AdminGroup", "ApproverGroup", "SourceUser", "TargetUser"],
            d.Notifications.Select(n => n.RecipientType).Order());

        await Dispatch();
        var approverMail = Assert.Single(Mail.Sent, m => m.To.Contains("mdel-approvers@contoso.local"));
        Assert.Contains("fără aprobare", approverMail.Subject);
    }

    [Fact]
    public async Task Missing_group_address_is_recorded_as_an_error_without_retries()
    {
        var d = await Create(Admin(), Request(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2)));
        var n = Assert.Single(d.Notifications);
        Assert.Contains("not configured", n.Error);

        Assert.Equal(0, await Dispatch());
        Assert.Empty(Mail.Sent);
    }

    [Fact]
    public async Task Failed_sends_are_retried_and_the_error_is_kept()
    {
        await ConfigureGroups();
        Mail.FailFor["mdel-approvers@contoso.local"] = true;
        var d = await Create(Admin(), Request(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2)));

        await Dispatch();
        var row = _factory.WithDb(db => db.DelegationNotifications.Single(x => x.DelegationId == d.Id));
        Assert.Null(row.SentUtc);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("550", row.Error);

        // A doua încercare doar după pauza de backoff: o forțăm mutând ultima încercare în trecut.
        Mail.FailFor.Clear();
        _factory.WithDb(db =>
        {
            db.DelegationNotifications.Single(x => x.DelegationId == d.Id).LastAttemptUtc = DateTime.UtcNow.AddMinutes(-5);
            return db.SaveChanges();
        });
        await Dispatch();

        row = _factory.WithDb(db => db.DelegationNotifications.Single(x => x.DelegationId == d.Id));
        Assert.NotNull(row.SentUtc);
        Assert.Equal(2, row.Attempts);
        Assert.Null(row.Error);
    }

    [Fact]
    public async Task Inform_group_members_are_notified_in_their_language()
    {
        await ConfigureGroups();
        var admin = AdminApprover();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/inform-group", new { samAccountName = "dev.approver" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/inform-group", new { samAccountName = "nobody" })).StatusCode);

        // dev.approver preferă engleza.
        var approver = Approver();
        await approver.PutAsJsonAsync("/api/me/language", new { language = "en" });

        var d = await Create(Admin(), Request(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2)));
        Assert.Contains(d.Notifications, n => n.RecipientType == "InformGroup" && n.RecipientEmail == "dev.approver@contoso.local");

        await Dispatch();
        var mail = Assert.Single(Mail.Sent, m => m.To.Contains("dev.approver@contoso.local"));
        Assert.StartsWith("[MDel] Forwarding request registered", mail.Subject);

        var members = await admin.GetFromJsonAsync<JsonElement>("/api/admin/inform-group");
        var memberId = members[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/inform-group/{memberId}")).StatusCode);
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>("/api/admin/inform-group")).GetArrayLength());
        Assert.True(_factory.WithDb(db => db.AuditLogs.Count(a => a.Action.StartsWith("InformGroup.")) == 2));
    }

    [Fact]
    public async Task Stopping_notifies_admins_source_and_target()
    {
        await ConfigureGroups();
        var d = await Create(Admin(), Request(DateTime.UtcNow, DateTime.UtcNow.AddDays(1), approval: false));
        var stopped = await (await Admin().PostAsJsonAsync($"/api/delegations/{d.Id}/stop", new { reason = "Revenire" }))
            .Content.ReadFromJsonAsync<DetailDto>(Json);

        Assert.Equal(["AdminGroup", "SourceUser", "TargetUser"],
            stopped!.Notifications.Where(n => n.Event == "Stopped").Select(n => n.RecipientType).Order());
    }

    [Fact]
    public async Task Stats_count_statuses_and_expiring_delegations()
    {
        await Create(Admin(), Request(DateTime.UtcNow, DateTime.UtcNow.AddDays(3), approval: false));
        await Create(Admin(), Request(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2), from: "maria.ionescu", to: "dev.approver"));

        var stats = await AdminApprover().GetFromJsonAsync<JsonElement>("/api/delegations/stats");
        Assert.Equal(1, stats.GetProperty("byStatus").GetProperty("Active").GetInt32());
        Assert.Equal(1, stats.GetProperty("byStatus").GetProperty("PendingApproval").GetInt32());
        Assert.Equal(1, stats.GetProperty("expiringIn7Days").GetInt32());
    }
}
