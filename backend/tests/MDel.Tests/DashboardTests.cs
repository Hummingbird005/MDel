using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MDel.Tests;

public sealed class DashboardTests : IDisposable
{
    private readonly MDelApiFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-Groups", "CONTOSO\\MDel-Admins");
        return c;
    }

    private async Task<int> Create(string from, string to, DateTime start, DateTime end, bool approval = false, string? notes = null)
    {
        var r = await Admin().PostAsJsonAsync("/api/delegations",
            new { fromSam = from, toSam = to, startUtc = start, endUtc = end, requiresApproval = approval, notes });
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private async Task<int[]> Ids(string query) =>
        (await Admin().GetFromJsonAsync<JsonElement>("/api/delegations?" + query))
        .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToArray();

    [Fact]
    public async Task List_can_be_sorted_and_filtered_by_period()
    {
        var now = DateTime.UtcNow;
        var a = await Create("ion.popescu", "maria.ionescu", now.AddDays(1), now.AddDays(10));
        var b = await Create("maria.ionescu", "dev.approver", now.AddDays(3), now.AddDays(4));
        var c = await Create("dev.approver", "ion.popescu", now.AddDays(20), now.AddDays(21));

        Assert.Equal([a, b, c], await Ids("sort=Start"));
        Assert.Equal([c, a, b], await Ids("sort=End&desc=true"));
        Assert.Equal([c, b, a], await Ids("sort=Id&desc=true"));

        // Intervalul păstrează delegările care se suprapun cu el.
        var from = now.AddDays(5).ToString("O");
        var to = now.AddDays(6).ToString("O");
        Assert.Equal([a], await Ids($"from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}"));
    }

    [Fact]
    public async Task Upcoming_lists_activations_and_expirations_in_order()
    {
        var now = DateTime.UtcNow;
        var expiring = await Create("ion.popescu", "maria.ionescu", now, now.AddDays(2));
        var starting = await Create("maria.ionescu", "dev.approver", now.AddDays(1), now.AddDays(30), approval: true);
        await Create("dev.approver", "ion.popescu", now.AddDays(20), now.AddDays(21)); // în afara ferestrei de 7 zile

        var events = await Admin().GetFromJsonAsync<JsonElement>("/api/delegations/upcoming?days=7");
        var list = events.EnumerateArray().Select(e => (e.GetProperty("type").GetString(), e.GetProperty("delegation").GetProperty("id").GetInt32())).ToList();

        Assert.Equal([("Activation", starting), ("Expiration", expiring)], list);
    }

    [Fact]
    public async Task Export_is_excel_friendly_csv_with_formula_injection_neutralized()
    {
        var now = DateTime.UtcNow;
        await Create("ion.popescu", "maria.ionescu", now.AddDays(1), now.AddDays(2));

        var response = await Admin().GetAsync("/api/delegations/export?sort=Id");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(l => l.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToArray();
        Assert.StartsWith("Id;Stare;Sursă", lines[0]);
        Assert.Contains("Ion Popescu", lines[1]);
        Assert.Contains("Programată", lines[1]);
        Assert.True(_factory.WithDb(db => db.AuditLogs.Any(a => a.Action == "Delegation.Exported")));

        Assert.Equal("'=cmd", ExportEscape("=cmd"));
    }

    private static string ExportEscape(string value)
    {
        var item = new MDel.Infrastructure.Delegations.DelegationListItem(1, MDel.Domain.Enums.DelegationStatus.Active,
            new(1, "a", value, null), new(2, "b", "B", null), DateTime.UtcNow, DateTime.UtcNow, false, true,
            new(1, "a", "A", null), DateTime.UtcNow, 0);
        var text = Encoding.UTF8.GetString(MDel.Infrastructure.Delegations.DelegationCsv.Write([item])[3..]);
        return text.Split('\n')[1].Split(';')[2];
    }
}
