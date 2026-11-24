using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Tests;

/// <summary>Bug-uri găsite la verificarea codului: forwardul trebuie să rămână corect în Exchange în orice situație.</summary>
public sealed class ForwardingSafetyTests : IDisposable
{
    private readonly ClockedApiFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private const string Source = "ion.popescu@contoso.local";
    private DateTime Now => _factory.Clock.GetUtcNow().UtcDateTime;
    private FakeExchangeService Exchange => (FakeExchangeService)_factory.Services.GetRequiredService<IExchangeService>();

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Admins,CONTOSO\\MDel-System");
        return c;
    }

    private async Task ConfigureGroups() => Assert.Equal(HttpStatusCode.NoContent, (await Admin().PutAsJsonAsync("/api/admin/settings", new
    {
        values = new Dictionary<string, string>
        {
            [SettingKeys.GroupApproversEmail] = "mdel-approvers@contoso.local",
            [SettingKeys.GroupAdminsEmail] = "mdel-admins@contoso.local",
        },
    })).StatusCode);

    private async Task<int> Create(DateTime start, DateTime end, string to = "maria.ionescu")
    {
        var r = await Admin().PostAsJsonAsync("/api/delegations",
            new { fromSam = "ion.popescu", toSam = to, startUtc = start, endUtc = end, requiresApproval = false });
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private async Task<JsonElement> Get(int id) => await Admin().GetFromJsonAsync<JsonElement>($"/api/delegations/{id}");

    private async Task<SchedulerRunResult> Run()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DelegationScheduler>().RunAsync(default);
    }

    /// <summary>Ce trimite formularul: ora locală din câmpul datetime-local, deci la precizie de minut.</summary>
    private static DateTime AsFormSends(DateTime utc) => new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_end_of_an_active_delegation_started_now_can_be_changed_from_the_form()
    {
        await ConfigureGroups();
        _factory.Clock.Advance(TimeSpan.FromSeconds(37 - _factory.Clock.GetUtcNow().Second)); // „acum” cu secunde
        var id = await Create(Now, Now.AddHours(2));
        var d = await Get(id);
        Assert.Equal("Active", d.GetProperty("status").GetString());

        var start = d.GetProperty("startUtc").GetDateTime();
        var newEnd = AsFormSends(Now.AddHours(6));
        var r = await Admin().PutAsJsonAsync($"/api/delegations/{id}", new
        {
            fromSam = "ion.popescu", toSam = "maria.ionescu", startUtc = AsFormSends(start), endUtc = newEnd,
            requiresApproval = false, rowVersion = d.GetProperty("rowVersion").GetString(),
        });
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        Assert.Equal(newEnd, (await Get(id)).GetProperty("endUtc").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task Extending_an_active_delegation_over_the_next_one_on_the_same_mailbox_is_refused()
    {
        await ConfigureGroups();
        var active = await Create(Now, Now.AddHours(2));
        var next = await Create(Now.AddHours(3), Now.AddHours(6), to: "dev.admin");
        var d = await Get(active);

        var r = await Admin().PutAsJsonAsync($"/api/delegations/{active}", new
        {
            fromSam = "ion.popescu", toSam = "maria.ionescu", startUtc = d.GetProperty("startUtc").GetDateTime(),
            endUtc = Now.AddHours(4), requiresApproval = false, rowVersion = d.GetProperty("rowVersion").GetString(),
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains($"#{next}", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_previous_forward_to_an_external_contact_is_restored_when_the_delegation_ends()
    {
        await ConfigureGroups();
        // Înainte de delegare, căsuța avea un forward manual către un contact extern.
        Exchange.Seed(Source, new MailboxForwarding("partener@extern.ro", true));
        var id = await Create(Now, Now.AddHours(1));
        Assert.Equal("maria.ionescu@contoso.local", (await Exchange.GetForwardingAsync(Source, default)).ForwardingAddress);

        _factory.Clock.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, (await Run()).Expired);

        Assert.Equal("Expired", (await Get(id)).GetProperty("status").GetString());
        var restored = await Exchange.GetForwardingAsync(Source, default);
        Assert.Equal("partener@extern.ro", restored.ForwardingAddress);
        Assert.True(restored.DeliverToMailboxAndForward);
    }

    [Fact]
    public async Task The_next_delegation_waits_until_the_previous_forward_on_the_same_mailbox_is_removed()
    {
        await ConfigureGroups();
        var first = await Create(Now, Now.AddHours(1));
        var second = await Create(Now.AddHours(1), Now.AddHours(3), to: "dev.admin");

        // La sfârșitul primei delegări, eliminarea forwardului eșuează o dată (eroare trecătoare).
        _factory.Clock.Advance(TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)));
        Exchange.FailNextSets = 1;
        await Run();
        Assert.Equal("Failed", (await Get(first)).GetProperty("status").GetString());
        // A doua nu se activează peste forwardul primei (altfel ar salva forwardul primei ca „forward anterior”).
        Assert.Equal("Scheduled", (await Get(second)).GetProperty("status").GetString());

        // După reîncercare: prima își reface starea inițială, apoi a doua se activează.
        _factory.Clock.Advance(DelegationScheduler.RetryDelay.Add(TimeSpan.FromSeconds(1)));
        await Run();
        Assert.Equal("Expired", (await Get(first)).GetProperty("status").GetString());
        await Run();
        var activeSecond = await Get(second);
        Assert.Equal("Active", activeSecond.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, activeSecond.GetProperty("previousForwardingAddress").ValueKind);

        // La final, căsuța revine la starea de dinainte de ambele delegări (fără forward).
        _factory.Clock.Advance(TimeSpan.FromHours(3));
        await Run();
        Assert.Null((await Exchange.GetForwardingAsync(Source, default)).ForwardingAddress);
    }
}
