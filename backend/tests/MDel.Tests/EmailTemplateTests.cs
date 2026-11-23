using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Tests;

public sealed class EmailTemplateTests : IDisposable
{
    private readonly MDelApiFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Admins");
        return c;
    }

    private FakeMailService Mail => (FakeMailService)_factory.Services.GetRequiredService<IMailService>();

    private Task<HttpResponseMessage> Save(Dictionary<string, string> values) =>
        Admin().PutAsJsonAsync("/api/admin/settings", new { values });

    private async Task CreateRequest()
    {
        var r = await Admin().PostAsJsonAsync("/api/delegations", new
        {
            fromSam = "ion.popescu", toSam = "maria.ionescu", notes = "Concediu <b>odihnă</b>",
            startUtc = DateTime.UtcNow.AddDays(1), endUtc = DateTime.UtcNow.AddDays(3), requiresApproval = true,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        await _factory.Services.GetRequiredService<NotificationDispatcher>().ProcessPendingAsync(default);
    }

    [Fact]
    public async Task Defaults_are_seeded_and_listed_with_placeholders()
    {
        var meta = await Admin().GetFromJsonAsync<JsonElement>("/api/admin/templates");
        Assert.Equal(9, meta.GetProperty("templates").GetArrayLength());
        Assert.Contains("sursa", meta.GetProperty("placeholders").EnumerateArray().Select(p => p.GetString()));

        var settings = await Admin().GetFromJsonAsync<JsonElement>("/api/admin/settings");
        var request = settings.GetProperty("settings").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "Templates.Request.Subject");
        Assert.Equal("Templates", request.GetProperty("category").GetString());
        Assert.StartsWith("[MDel] Cerere de aprobare", request.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Custom_romanian_template_is_used_and_text_is_escaped()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Save(new()
        {
            [SettingKeys.GroupApproversEmail] = "mdel-approvers@contoso.local",
            ["Templates.Request.Subject"] = "Aprobare necesară #{id}: {sursa} → {destinatar}",
            ["Templates.Request.Body"] = "Bună ziua,\n\nVă rugăm să aprobați până la {inceput}.\nMotiv: {observatii}",
        })).StatusCode);

        await CreateRequest();

        var mail = Assert.Single(Mail.Sent, m => m.To.Contains("mdel-approvers@contoso.local"));
        Assert.Matches(@"^Aprobare necesară #\d+: Ion Popescu → Maria Ionescu$", mail.Subject);
        Assert.Contains("<p style=\"margin:0 0 12px\">Bună ziua,</p>", mail.HtmlBody);
        Assert.Contains("Motiv: Concediu &lt;b&gt;odihnă&lt;/b&gt;", mail.HtmlBody); // escapat, fără HTML injectat
        Assert.DoesNotContain("<b>odihnă</b>", mail.HtmlBody);
    }

    [Fact]
    public async Task English_recipients_keep_the_built_in_english_text()
    {
        await Save(new() { ["Templates.Pending.Subject"] = "Doar în română #{id}" });
        await Admin().PostAsJsonAsync("/api/admin/inform-group", new { samAccountName = "dev.approver" });
        var approver = _factory.CreateClient();
        approver.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\dev.approver");
        approver.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Approvers");
        await approver.PutAsJsonAsync("/api/me/language", new { language = "en" });

        await CreateRequest();

        var mail = Assert.Single(Mail.Sent, m => m.To.Contains("dev.approver@contoso.local"));
        Assert.StartsWith("[MDel] Forwarding request registered", mail.Subject);
    }

    [Fact]
    public async Task Unknown_fields_are_rejected_and_empty_falls_back_to_default()
    {
        var bad = await Save(new() { ["Templates.Approved.Body"] = "Aprobat de {aprobator}" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("{aprobator}", await bad.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Save(new() { ["Templates.Approved.Subject"] = "Linia 1\nLinia 2" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Save(new()
        {
            [SettingKeys.GroupApproversEmail] = "mdel-approvers@contoso.local",
            ["Templates.Request.Subject"] = "",
            ["Templates.Request.Body"] = "",
        })).StatusCode);
        await CreateRequest();
        Assert.StartsWith("[MDel] Cerere de aprobare", Assert.Single(Mail.Sent).Subject);
    }

    [Fact]
    public async Task Preview_renders_a_sample_delegation()
    {
        var response = await Admin().PostAsJsonAsync("/api/admin/templates/preview",
            new { kind = "Stopped", subject = "Oprit: {sursa}", body = "Motiv: {motiv}" });
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Oprit: Ion Popescu", preview.GetProperty("subject").GetString());
        Assert.Contains("Motiv: Revenire anticipată din concediu", preview.GetProperty("html").GetString());

        var invalid = await Admin().PostAsJsonAsync("/api/admin/templates/preview", new { kind = "Stopped", subject = "{x}" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
