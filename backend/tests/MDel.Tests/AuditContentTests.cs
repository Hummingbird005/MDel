using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MDel.Tests;

/// <summary>Auditul scrie exact ce a făcut utilizatorul: valorile introduse, vechi → nou, încercările respinse.</summary>
public sealed class AuditContentTests : IDisposable
{
    private readonly MDelApiFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private HttpClient Client(string user, string groups)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-User", $"CONTOSO\\{user}");
        c.DefaultRequestHeaders.Add("X-Dev-Groups", groups);
        return c;
    }

    private HttpClient Admin() => Client("dev.admin", "CONTOSO\\MDel-Admins,CONTOSO\\MDel-System");

    private HttpClient Approver() => Client("dev.approver", "CONTOSO\\MDel-Approvers");

    private static async Task<int> Create(HttpClient c, string from = "ion.popescu", string to = "maria.ionescu", int startDays = 1, int endDays = 2,
        string? notes = "Concediu medical – răspunde Maria")
    {
        var r = await c.PostAsJsonAsync("/api/delegations", new
        {
            fromSam = from, toSam = to, startUtc = DateTime.UtcNow.AddDays(startDays), endUtc = DateTime.UtcNow.AddDays(endDays),
            requiresApproval = true, notes,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static async Task<JsonElement> Entry(HttpClient c, string action, int id)
    {
        var page = await c.GetFromJsonAsync<JsonElement>($"/api/audit?action={action}&entityType=Delegation&entityId={id}");
        return page.GetProperty("items").EnumerateArray().First();
    }

    private static JsonElement Values(JsonElement entry, string which) => JsonDocument.Parse(entry.GetProperty(which).GetString()!).RootElement;

    [Fact]
    public async Task Creating_a_delegation_records_every_value_typed_in_the_form()
    {
        var admin = Admin();
        var id = await Create(admin);

        var created = await Entry(admin, "Delegation.Created", id);
        var v = Values(created, "newValues"); // valorile sunt în lista de audit, fără a deschide detaliul
        Assert.Equal("ion.popescu", v.GetProperty("From").GetString());
        Assert.Equal("maria.ionescu", v.GetProperty("To").GetString());
        Assert.True(v.GetProperty("RequiresApproval").GetBoolean());
        Assert.Equal("Concediu medical – răspunde Maria", v.GetProperty("Notes").GetString());
        Assert.Equal("PendingApproval", v.GetProperty("Status").GetString());
        Assert.True(v.TryGetProperty("StartUtc", out _) && v.TryGetProperty("EndUtc", out _));
        Assert.Equal("CONTOSO\\dev.admin", created.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Updating_a_delegation_records_old_and_new_values()
    {
        var admin = Admin();
        var id = await Create(admin);
        var view = await admin.GetFromJsonAsync<JsonElement>($"/api/delegations/{id}");

        var update = await admin.PutAsJsonAsync($"/api/delegations/{id}", new
        {
            fromSam = "ion.popescu", toSam = "dev.admin",
            startUtc = DateTime.UtcNow.AddDays(1), endUtc = DateTime.UtcNow.AddDays(5), requiresApproval = true,
            notes = "Prelungit", rowVersion = view.GetProperty("rowVersion").GetString(),
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var updated = await Entry(admin, "Delegation.Updated", id);
        var before = Values(updated, "oldValues");
        var after = Values(updated, "newValues");
        Assert.Equal("maria.ionescu", before.GetProperty("To").GetString());
        Assert.Equal("dev.admin", after.GetProperty("To").GetString());
        Assert.Equal("Concediu medical – răspunde Maria", before.GetProperty("Notes").GetString());
        Assert.Equal("Prelungit", after.GetProperty("Notes").GetString());
        Assert.NotEqual(before.GetProperty("EndUtc").GetString(), after.GetProperty("EndUtc").GetString());
    }

    [Fact]
    public async Task Approve_reject_and_stop_record_the_status_change_and_the_reason()
    {
        var admin = Admin();
        var approver = Approver();

        var approved = await Create(admin);
        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsJsonAsync($"/api/delegations/{approved}/approve", new { })).StatusCode);
        var a = await Entry(admin, "Delegation.Approved", approved);
        Assert.Equal("PendingApproval", Values(a, "oldValues").GetProperty("Status").GetString());
        Assert.Equal("Scheduled", Values(a, "newValues").GetProperty("Status").GetString());
        Assert.Equal("CONTOSO\\dev.approver", Values(a, "newValues").GetProperty("ApprovedBy").GetString());

        var rejected = await Create(admin, "dev.approver", "maria.ionescu");
        Assert.Equal(HttpStatusCode.OK, (await approver.PostAsJsonAsync($"/api/delegations/{rejected}/reject", new { reason = "Lipsește acordul șefului" })).StatusCode);
        var r = await Entry(admin, "Delegation.Rejected", rejected);
        Assert.Equal("PendingApproval", Values(r, "oldValues").GetProperty("Status").GetString());
        Assert.Equal("Rejected", Values(r, "newValues").GetProperty("Status").GetString());
        Assert.Equal("Lipsește acordul șefului", Values(r, "newValues").GetProperty("RejectReason").GetString());

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/delegations/{approved}/stop", new { reason = "Revenit din concediu" })).StatusCode);
        var s = await Entry(admin, "Delegation.Stopped", approved);
        Assert.Equal("Scheduled", Values(s, "oldValues").GetProperty("Status").GetString());
        Assert.Equal("Stopped", Values(s, "newValues").GetProperty("Status").GetString());
        Assert.Equal("Revenit din concediu", Values(s, "newValues").GetProperty("StopReason").GetString());
    }

    [Fact]
    public async Task A_refused_attempt_is_audited_with_the_values_typed_and_the_reason()
    {
        var admin = Admin();
        await Create(admin);

        // Aceeași căsuță sursă, același interval: refuzată – dar rămâne în audit, cu tot ce s-a introdus.
        var overlap = await admin.PostAsJsonAsync("/api/delegations", new
        {
            fromSam = "ion.popescu", toSam = "dev.admin", startUtc = DateTime.UtcNow.AddDays(1), endUtc = DateTime.UtcNow.AddDays(2),
            requiresApproval = false, notes = "Încercare a doua",
        });
        Assert.NotEqual(HttpStatusCode.Created, overlap.StatusCode);

        var page = await admin.GetFromJsonAsync<JsonElement>("/api/audit?action=Delegation.CreateFailed");
        var failed = page.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Failure", failed.GetProperty("result").GetString());
        Assert.Contains("#", failed.GetProperty("details").GetString()); // motivul: suprapunere cu delegarea #…
        var v = Values(failed, "newValues");
        Assert.Equal("ion.popescu", v.GetProperty("From").GetString());
        Assert.Equal("dev.admin", v.GetProperty("To").GetString());
        Assert.False(v.GetProperty("RequiresApproval").GetBoolean());
        Assert.Equal("Încercare a doua", v.GetProperty("Notes").GetString());

        // Validare de câmpuri: sfârșit înaintea începutului.
        var invalid = await admin.PostAsJsonAsync("/api/delegations", new
        {
            fromSam = "dev.approver", toSam = "maria.ionescu", startUtc = DateTime.UtcNow.AddDays(3), endUtc = DateTime.UtcNow.AddDays(2),
            requiresApproval = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/audit?action=Delegation.CreateFailed");
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Contains(all.GetProperty("items").EnumerateArray(), i => i.GetProperty("details").GetString()!.Contains("EndUtc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Approving_twice_leaves_the_refused_attempt_in_the_audit()
    {
        var admin = Admin();
        var id = await Create(admin);
        Assert.Equal(HttpStatusCode.OK, (await Approver().PostAsJsonAsync($"/api/delegations/{id}/approve", new { })).StatusCode);
        var second = Client("dev.approver2", "CONTOSO\\MDel-Approvers");
        Assert.Equal(HttpStatusCode.Conflict, (await second.PostAsJsonAsync($"/api/delegations/{id}/approve", new { })).StatusCode);

        var failed = await Entry(admin, "Delegation.ApproveFailed", id);
        Assert.Equal("Failure", failed.GetProperty("result").GetString());
        Assert.Equal("CONTOSO\\dev.approver2", failed.GetProperty("userName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(failed.GetProperty("details").GetString()));
    }

    [Fact]
    public async Task Successful_http_calls_are_hidden_by_default_but_denied_and_failed_ones_stay()
    {
        var admin = Admin();
        await Create(admin);
        // O încercare refuzată (403): utilizator fără rol.
        var nobody = Client("ion.popescu", "none");
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.PostAsJsonAsync("/api/delegations", new { })).StatusCode);

        var items = (await admin.GetFromJsonAsync<JsonElement>("/api/audit")).GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(items, i => i.GetProperty("action").GetString() == "Http.Request" && i.GetProperty("result").GetString() == "Success");
        Assert.Contains(items, i => i.GetProperty("action").GetString() == "Http.Request" && i.GetProperty("result").GetString() == "Denied");
        Assert.Contains(items, i => i.GetProperty("action").GetString() == "Delegation.Created");

        // La cerere, apar și apelurile tehnice reușite; la fel când se filtrează explicit „Http.”.
        var withHttp = (await admin.GetFromJsonAsync<JsonElement>("/api/audit?includeHttp=true")).GetProperty("items").EnumerateArray();
        Assert.Contains(withHttp, i => i.GetProperty("action").GetString() == "Http.Request" && i.GetProperty("result").GetString() == "Success");
        var onlyHttp = (await admin.GetFromJsonAsync<JsonElement>("/api/audit?action=Http.Request&result=Success")).GetProperty("total").GetInt32();
        Assert.True(onlyHttp > 0);
    }

    [Fact]
    public async Task The_audit_can_be_searched_by_a_value_that_was_typed()
    {
        var admin = Admin();
        var id = await Create(admin, notes: "Referință-ZX-4471");

        var found = (await admin.GetFromJsonAsync<JsonElement>("/api/audit?search=ZX-4471")).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(found, i => i.GetProperty("entityId").GetString() == id.ToString());
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>("/api/audit?search=nu-exista-1234")).GetProperty("items").EnumerateArray());
    }
}
