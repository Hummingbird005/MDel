using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Domain.Enums;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Scheduling;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MDel.Tests;

/// <summary>Ceas de test: pornește de la ora reală și poate fi avansat.</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

public sealed class ClockedApiFactory : MDelApiFactory
{
    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock)));
    }
}

public sealed class SchedulerTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly ClockedApiFactory _factory = new();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "mdel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _factory.Dispose();
        try { Directory.Delete(_storage, true); } catch { /* ignore */ }
    }

    private sealed record NotificationDto(string RecipientType, string Event);
    private sealed record AttachmentDto(int Id, bool Purged);
    private sealed record DetailDto(int Id, string Status, string? LastError, string? PreviousForwardingAddress,
        NotificationDto[] Notifications, AttachmentDto[] Attachments);

    private DateTime Now => _factory.Clock.GetUtcNow().UtcDateTime;
    private FakeExchangeService Exchange => (FakeExchangeService)_factory.Services.GetRequiredService<IExchangeService>();

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Admins,CONTOSO\\MDel-System");
        return c;
    }

    private async Task<SchedulerRunResult> Run()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DelegationScheduler>().RunAsync(default);
    }

    private async Task<int> Reconcile()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DelegationScheduler>().ReconcileAsync(default);
    }

    private async Task<DetailDto> Get(int id) => (await Admin().GetFromJsonAsync<DetailDto>($"/api/delegations/{id}", Json))!;

    private async Task ConfigureGroups() => Assert.Equal(HttpStatusCode.NoContent, (await Admin().PutAsJsonAsync("/api/admin/settings", new
    {
        values = new Dictionary<string, string>
        {
            [SettingKeys.GroupApproversEmail] = "mdel-approvers@contoso.local",
            [SettingKeys.GroupAdminsEmail] = "mdel-admins@contoso.local",
            [SettingKeys.AttachmentsStoragePath] = _storage,
        },
    })).StatusCode);

    private async Task<int> Create(DateTime start, DateTime end, bool approval = false)
    {
        var response = await Admin().PostAsJsonAsync("/api/delegations",
            new { fromSam = "ion.popescu", toSam = "maria.ionescu", startUtc = start, endUtc = end, requiresApproval = approval });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DetailDto>(Json))!.Id;
    }

    [Fact]
    public async Task Scheduled_delegation_is_activated_at_start_and_expired_at_end()
    {
        await ConfigureGroups();
        Exchange.Seed("ion.popescu@contoso.local", new MailboxForwarding("contoso.local/Users/Old Target", false));
        var id = await Create(Now.AddHours(1), Now.AddHours(5));

        Assert.Equal(new SchedulerRunResult(0, 0, 0, 0), await Run()); // încă nu e momentul
        Assert.Equal("Scheduled", (await Get(id)).Status);

        _factory.Clock.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, (await Run()).Activated);

        var active = await Get(id);
        Assert.Equal("Active", active.Status);
        Assert.Equal("maria.ionescu@contoso.local", (await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default)).ForwardingAddress);
        Assert.Equal(["AdminGroup", "SourceUser", "TargetUser"],
            active.Notifications.Where(n => n.Event == "Activated").Select(n => n.RecipientType).Order());

        _factory.Clock.Advance(TimeSpan.FromHours(4));
        Assert.Equal(1, (await Run()).Expired);

        var expired = await Get(id);
        Assert.Equal("Expired", expired.Status);
        Assert.Equal("contoso.local/Users/Old Target", (await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default)).ForwardingAddress);
        Assert.Equal(["AdminGroup", "SourceUser", "TargetUser"],
            expired.Notifications.Where(n => n.Event == "Expired").Select(n => n.RecipientType).Order());
        Assert.True(_factory.WithDb(db => db.AuditLogs.Any(a => a.Action == "Delegation.ForwardRemoved" && a.UserName == "system")));
    }

    [Fact]
    public async Task Request_not_approved_before_the_end_expires_without_touching_exchange()
    {
        var id = await Create(Now.AddHours(1), Now.AddHours(2), approval: true);
        _factory.Clock.Advance(TimeSpan.FromHours(3));

        Assert.Equal(1, (await Run()).Expired);
        var d = await Get(id);
        Assert.Equal("Expired", d.Status);
        Assert.DoesNotContain(d.Notifications, n => n.Event == "Expired");
        Assert.Null((await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default)).ForwardingAddress);
    }

    [Fact]
    public async Task Exchange_failure_marks_failed_notifies_admins_once_and_is_retried_later()
    {
        await ConfigureGroups();
        var id = await Create(Now.AddMinutes(30), Now.AddDays(1));
        Exchange.FailSet = true;
        _factory.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(1, (await Run()).Failed);
        var failed = await Get(id);
        Assert.Equal("Failed", failed.Status);
        Assert.Contains("not reachable", failed.LastError);
        Assert.Single(failed.Notifications, n => n.Event == "Failed" && n.RecipientType == "AdminGroup");

        // Înainte de pauza de reîncercare nu se încearcă din nou.
        Exchange.FailSet = false;
        _factory.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(new SchedulerRunResult(0, 0, 0, 0), await Run());

        _factory.Clock.Advance(DelegationScheduler.RetryDelay);
        Assert.Equal(1, (await Run()).Retried);
        var active = await Get(id);
        Assert.Equal("Active", active.Status);
        Assert.Null(active.LastError);
        Assert.Single(active.Notifications, n => n.Event == "Failed");
        Assert.Contains(active.Notifications, n => n.Event == "Activated");
    }

    [Fact]
    public async Task Failed_removal_at_expiry_is_retried_until_the_forward_is_restored()
    {
        var id = await Create(Now, Now.AddHours(1));
        Assert.Equal("Active", (await Get(id)).Status);

        Exchange.FailSet = true;
        _factory.Clock.Advance(TimeSpan.FromHours(2));
        await Run();
        Assert.Equal("Failed", (await Get(id)).Status);
        Assert.Equal("maria.ionescu@contoso.local", (await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default)).ForwardingAddress);

        Exchange.FailSet = false;
        _factory.Clock.Advance(DelegationScheduler.RetryDelay);
        await Run();
        Assert.Equal("Expired", (await Get(id)).Status);
        Assert.Null((await Exchange.GetForwardingAsync("ion.popescu@contoso.local", default)).ForwardingAddress);
    }

    [Fact]
    public async Task Reconciliation_reports_forwards_changed_outside_the_app_once()
    {
        await ConfigureGroups();
        var id = await Create(Now, Now.AddDays(1));
        Assert.Equal(0, await Reconcile());

        Exchange.Seed("ion.popescu@contoso.local", new MailboxForwarding("someone.else@contoso.local", true));
        Assert.Equal(1, await Reconcile());
        Assert.Equal(0, await Reconcile()); // deja raportat

        var d = await Get(id);
        Assert.Contains("changed outside MDel", d.LastError);
        Assert.Single(d.Notifications, n => n.Event == "Failed");
        Assert.True(_factory.WithDb(db => db.AuditLogs.Any(a => a.Action == "Delegation.DriftDetected")));
    }

    [Fact]
    public async Task Retention_purges_old_files_and_audit_entries()
    {
        await ConfigureGroups();
        var id = await Create(Now.AddHours(1), Now.AddDays(2));
        var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "files", "aprobare.pdf" } };
        Assert.Equal(HttpStatusCode.OK, (await Admin().PostAsync($"/api/delegations/{id}/attachments", form)).StatusCode);
        var auditBefore = _factory.WithDb(db => db.AuditLogs.Count());
        Assert.NotEmpty(Directory.GetFiles(_storage, "*", SearchOption.AllDirectories));

        _factory.Clock.Advance(TimeSpan.FromDays(366));
        using (var scope = _factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(default);
            Assert.Equal(1, result.AttachmentsPurged);
            Assert.Equal(auditBefore, result.AuditEntriesPurged);
        }

        Assert.Empty(Directory.GetFiles(_storage, "*", SearchOption.AllDirectories));
        var d = await Get(id);
        Assert.True(Assert.Single(d.Attachments).Purged);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin().GetAsync($"/api/delegations/{id}/attachments/{d.Attachments[0].Id}")).StatusCode);
        Assert.True(_factory.WithDb(db => db.AuditLogs.Any(a => a.Action == "Retention.Purged")));
    }
}
