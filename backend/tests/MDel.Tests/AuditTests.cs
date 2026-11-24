using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MDel.Domain;
using MDel.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Tests;

public sealed class AuditTests : IDisposable
{
    private readonly MDelApiFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Admins,CONTOSO\\MDel-System");
        return c;
    }

    private async Task<int> CreateDelegation(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/delegations", new
        {
            fromSam = "ion.popescu", toSam = "maria.ionescu",
            startUtc = DateTime.UtcNow.AddDays(1), endUtc = DateTime.UtcNow.AddDays(2), requiresApproval = true,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Audit_lists_filters_and_shows_old_and_new_values()
    {
        var admin = Admin();
        var id = await CreateDelegation(admin);
        await admin.PutAsJsonAsync("/api/admin/settings", new { values = new Dictionary<string, string> { [SettingKeys.RetentionAuditDays] = "400" } });

        var all = await admin.GetFromJsonAsync<JsonElement>("/api/audit");
        Assert.True(all.GetProperty("total").GetInt32() >= 3);

        // Toată urma unei delegări.
        var trail = await admin.GetFromJsonAsync<JsonElement>($"/api/audit?entityType=Delegation&entityId={id}");
        Assert.Contains(trail.GetProperty("items").EnumerateArray(), i => i.GetProperty("action").GetString() == "Delegation.Created");

        // Prefix de acțiune + detaliu cu valori vechi/noi.
        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/audit?action=Settings.");
        var entry = Assert.Single(settings.GetProperty("items").EnumerateArray());
        Assert.True(entry.GetProperty("hasChanges").GetBoolean());
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/audit/{entry.GetProperty("id").GetInt64()}");
        Assert.Contains("365", detail.GetProperty("oldValues").GetString());
        Assert.Contains("400", detail.GetProperty("newValues").GetString());

        var denied = await admin.GetFromJsonAsync<JsonElement>("/api/audit?result=Denied");
        Assert.Equal(0, denied.GetProperty("total").GetInt32());

        // Login-ul are utilizatorul corect (nu „anonymous”).
        var logins = await admin.GetFromJsonAsync<JsonElement>("/api/audit?action=Auth.Login");
        Assert.All(logins.GetProperty("items").EnumerateArray(), i => Assert.Equal("CONTOSO\\dev.admin", i.GetProperty("userName").GetString()));

        var actions = await admin.GetFromJsonAsync<string[]>("/api/audit/actions");
        Assert.Contains("Delegation.Created", actions!);
    }

    [Fact]
    public async Task Audit_is_admin_only_and_export_is_audited()
    {
        var approver = _factory.CreateClient();
        approver.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\dev.approver");
        approver.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Approvers");
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.GetAsync("/api/audit")).StatusCode);

        var response = await Admin().GetAsync("/api/audit/export?action=Http.Request");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = Encoding.UTF8.GetString((await response.Content.ReadAsByteArrayAsync())[3..]);
        Assert.StartsWith("Id;TimestampUtc;User;Action", text);
        Assert.Contains("Http.Request", text);
        Assert.True(_factory.WithDb(db => db.AuditLogs.Any(a => a.Action == "Audit.Exported")));
    }

    [Fact]
    public async Task Failed_notification_can_be_retried_after_fixing_the_group_address()
    {
        var admin = Admin();
        await CreateDelegation(admin); // adresa grupului de aprobatori lipsește -> mesaj eșuat

        var failed = await admin.GetFromJsonAsync<JsonElement>("/api/admin/notifications?filter=Failed");
        var item = Assert.Single(failed.EnumerateArray());
        Assert.Equal(1, await admin.GetFromJsonAsync<int>("/api/admin/notifications/failed-count"));

        await admin.PutAsJsonAsync("/api/admin/settings",
            new { values = new Dictionary<string, string> { [SettingKeys.GroupApproversEmail] = "mdel-approvers@contoso.local" } });
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsync($"/api/admin/notifications/{item.GetProperty("id").GetInt64()}/retry", null)).StatusCode);

        await _factory.Services.GetRequiredService<NotificationDispatcher>().ProcessPendingAsync(default);
        var sent = await admin.GetFromJsonAsync<JsonElement>("/api/admin/notifications?filter=Sent");
        Assert.Equal("mdel-approvers@contoso.local", Assert.Single(sent.EnumerateArray()).GetProperty("recipientEmail").GetString());
        Assert.Equal(0, await admin.GetFromJsonAsync<int>("/api/admin/notifications/failed-count"));
    }
}
