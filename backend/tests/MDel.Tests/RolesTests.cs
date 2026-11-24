using System.Net;
using System.Net.Http.Json;
using MDel.Application.Common;
using MDel.Domain;

namespace MDel.Tests;

/// <summary>Cele trei roluri (Administrator, Aprobator, Administrator sistem): acces pe ecrane și adminul inițial.</summary>
public class RolesTests(MDelApiFactory factory) : IClassFixture<MDelApiFactory>
{
    private sealed record MeDto(string SamAccountName, string[] Roles);

    private const string Admins = "CONTOSO\\MDel-Admins";
    private const string Approvers = "CONTOSO\\MDel-Approvers";
    private const string System = "CONTOSO\\MDel-System";

    private HttpClient Client(string groups)
    {
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Dev-Groups", groups);
        return c;
    }

    // grupuri → (Setări, Audit, listă delegări, căutare utilizatori AD, roluri)
    [Theory]
    [InlineData(Admins, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.OK, HttpStatusCode.OK, "Admin")]
    [InlineData(Approvers, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.OK, HttpStatusCode.Forbidden, "Approver")]
    [InlineData(System, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.OK, "SystemAdmin")]
    [InlineData(Admins + "," + System, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, "Admin,SystemAdmin")]
    [InlineData(Admins + "," + Approvers + "," + System, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, "Admin,Approver,SystemAdmin")]
    [InlineData("none", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, "")]
    public async Task Each_role_opens_only_its_own_areas_and_roles_combine(
        string groups, HttpStatusCode settings, HttpStatusCode audit, HttpStatusCode delegations, HttpStatusCode directory, string roles)
    {
        var c = Client(groups);

        Assert.Equal(settings, (await c.GetAsync("/api/admin/settings")).StatusCode);
        Assert.Equal(audit, (await c.GetAsync("/api/audit")).StatusCode);
        Assert.Equal(delegations, (await c.GetAsync("/api/delegations")).StatusCode);
        Assert.Equal(directory, (await c.GetAsync("/api/directory/users?q=dev")).StatusCode);

        var me = await c.GetFromJsonAsync<MeDto>("/api/me");
        Assert.Equal(roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(), me!.Roles.Order());
    }

    [Fact]
    public async Task Initial_admin_keeps_each_role_only_until_its_group_is_configured()
    {
        var config = SetupTests.NoConfiguredGroups.ToDictionary(g => g.Key, g => g.Value);
        config["Setup:InitialAdmins:0"] = "CONTOSO\\boot.admin";
        await using var app = new SetupTests.SetupFactory(new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot(),
            Guid.NewGuid().ToString(), config);

        HttpClient As(string user, string groups)
        {
            var c = app.CreateClient();
            c.DefaultRequestHeaders.Add("X-Dev-User", user);
            c.DefaultRequestHeaders.Add("X-Dev-Groups", groups);
            return c;
        }

        // 1) Niciun grup configurat: adminul inițial are toate rolurile și configurează aplicația.
        var boot = As("CONTOSO\\boot.admin", "none");
        Assert.Equal(AppRoles.All.Order(), (await boot.GetFromJsonAsync<MeDto>("/api/me"))!.Roles.Order());

        // 2) Setează grupul de administratori de sistem din Setări.
        var save = await boot.PutAsJsonAsync("/api/admin/settings", new { values = new Dictionary<string, string?> { [SettingKeys.GroupSystemAdmins] = System } });
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);

        // 3) Adminul inițial pierde doar rolul al cărui grup e acum configurat; celelalte două grupuri sunt încă goale.
        var bootAfter = As("CONTOSO\\boot.admin", "other");
        Assert.Equal(new[] { AppRoles.Admin, AppRoles.Approver }.Order(), (await bootAfter.GetFromJsonAsync<MeDto>("/api/me"))!.Roles.Order());
        Assert.Equal(HttpStatusCode.Forbidden, (await bootAfter.GetAsync("/api/admin/settings")).StatusCode);

        // 4) Un membru al grupului nou are rolul, fără să fie admin inițial.
        var member = As("CONTOSO\\ion.popescu", System);
        Assert.Equal([AppRoles.SystemAdmin], (await member.GetFromJsonAsync<MeDto>("/api/me"))!.Roles);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/admin/settings")).StatusCode);

        // 5) Completează și celelalte grupuri: adminul inițial nu mai are niciun rol din bootstrap.
        var all = await member.PutAsJsonAsync("/api/admin/settings", new
        {
            values = new Dictionary<string, string?> { [SettingKeys.GroupAdmins] = Admins, [SettingKeys.GroupApprovers] = Approvers },
        });
        Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        var bootLast = As("CONTOSO\\boot.admin", "another");
        Assert.Empty((await bootLast.GetFromJsonAsync<MeDto>("/api/me"))!.Roles);
    }
}
