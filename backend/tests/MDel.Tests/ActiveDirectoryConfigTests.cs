using System.DirectoryServices.Protocols;
using System.Net.Http.Json;
using MDel.Infrastructure.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Tests;

/// <summary>Secțiunea ActiveDirectory din appsettings.json și alegerea parametrilor LDAP.</summary>
public class ActiveDirectoryConfigTests(MDelApiFactory factory) : IClassFixture<MDelApiFactory>
{
    [Fact]
    public void Ldaps_with_dn_bind_and_several_search_bases()
    {
        var info = LdapConnectionInfo.FromOptions(new ActiveDirectoryOptions
        {
            Enabled = true,
            Server = "dc01.corp.local",
            Port = 636,
            UseSsl = true,
            BindUser = "CN=svc-mdel,OU=Servicii,DC=corp,DC=local",
            BindPassword = "x",
            SearchBases = ["OU=Angajati,DC=corp,DC=local", " ", "OU=Externi,DC=corp,DC=local"],
        });

        Assert.Equal(("dc01.corp.local", 636, true), (info.Host, info.Port, info.UseSsl));
        Assert.Equal(AuthType.Basic, info.AuthType); // un DN se poate folosi doar cu simple bind
        Assert.Equal(["OU=Angajati,DC=corp,DC=local", "OU=Externi,DC=corp,DC=local"], info.SearchBases);
        Assert.Equal("appsettings.json", info.Source);
    }

    [Theory]
    [InlineData("CORP\\svc-mdel", null, AuthType.Negotiate)]
    [InlineData("svc-mdel@corp.local", null, AuthType.Negotiate)]
    [InlineData("svc-mdel@corp.local", "Basic", AuthType.Basic)]
    [InlineData("CN=svc,DC=corp,DC=local", null, AuthType.Basic)]
    [InlineData(null, null, AuthType.Negotiate)]
    public void Auth_type_is_chosen_from_the_bind_user(string? user, string? configured, AuthType expected) =>
        Assert.Equal(expected, LdapConnectionInfo.ResolveAuthType(configured, user));

    [Theory]
    [InlineData("dc01", false, null, "dc01", 389, false)]
    [InlineData("dc01", true, null, "dc01", 636, true)]
    [InlineData("dc01:3269", true, null, "dc01", 3269, true)]
    [InlineData("ldaps://dc01.corp.local", false, null, "dc01.corp.local", 636, true)]
    [InlineData("ldap://dc01:389/", false, null, "dc01", 389, false)]
    [InlineData("dc01:389", true, 636, "dc01", 636, true)]
    public void Server_port_and_ssl_are_parsed(string server, bool ssl, int? port, string host, int expectedPort, bool expectedSsl) =>
        Assert.Equal((host, expectedPort, expectedSsl), LdapConnectionInfo.ParseServer(server, ssl, port));

    [Fact]
    public void Simple_bind_without_ssl_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LdapConnectionInfo.FromOptions(new ActiveDirectoryOptions
        {
            Enabled = true, Server = "dc01", BindUser = "CN=svc,DC=corp,DC=local", BindPassword = "x",
        }));
        Assert.Contains("UseSsl", ex.Message);
    }

    [Fact]
    public void Missing_server_is_reported()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LdapConnectionInfo.FromOptions(new ActiveDirectoryOptions { Enabled = true }));
        Assert.Contains("ActiveDirectory:Server", ex.Message);
    }

    [Fact]
    public void Settings_screen_values_still_work()
    {
        var info = LdapConnectionInfo.FromSettings("", "contoso.local", "", "CONTOSO\\svc", "p");
        Assert.Equal(("contoso.local", 389, false, AuthType.Negotiate), (info.Host, info.Port, info.UseSsl, info.AuthType));
        Assert.Equal(["DC=contoso,DC=local"], info.SearchBases);
        Assert.Equal("Settings", info.Source);
    }

    [Theory]
    [InlineData(49, "80090308: LdapErr: DSID-0C0903A9, comment: AcceptSecurityContext error, data 52e, v1db1", "wrong user name or password")]
    [InlineData(49, "80090308: LdapErr: DSID-0C0903A9, comment: AcceptSecurityContext error, data 775, v1db1", "locked out")]
    [InlineData(81, null, "over SSL")]
    [InlineData(8, null, "UseSsl = true")]
    public void Ldap_errors_are_explained(int code, string? serverMessage, string expected)
    {
        var info = LdapConnectionInfo.FromOptions(new ActiveDirectoryOptions { Enabled = true, Server = "dc01", UseSsl = true });
        var message = LdapDirectoryService.Explain(new LdapException(code, "LDAP error", serverMessage), info);
        Assert.Contains(expected, message);
        Assert.Contains($"(LDAP {code})", message);
    }

    [Fact]
    public async Task Settings_api_shows_the_appsettings_connection_without_the_password()
    {
        using var configured = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ActiveDirectory:Enabled", "true");
            b.UseSetting("ActiveDirectory:Server", "dc01.corp.local");
            b.UseSetting("ActiveDirectory:Port", "636");
            b.UseSetting("ActiveDirectory:UseSsl", "true");
            b.UseSetting("ActiveDirectory:BindUser", "CN=svc-mdel,OU=Servicii,DC=corp,DC=local");
            b.UseSetting("ActiveDirectory:BindPassword", "Secret#1");
            b.UseSetting("ActiveDirectory:SearchBases:0", "OU=Angajati,DC=corp,DC=local");
        });

        var json = await configured.CreateClient().GetStringAsync("/api/admin/settings");
        Assert.Contains("\"server\":\"dc01.corp.local\"", json);
        Assert.Contains("\"authType\":\"Basic\"", json);
        Assert.Contains("\"hasBindPassword\":true", json);
        Assert.Contains("OU=Angajati,DC=corp,DC=local", json);
        Assert.DoesNotContain("Secret#1", json);

        // Fără secțiune activă: null (se folosește ecranul Setări).
        var plain = await factory.CreateClient().GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/settings");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, plain.GetProperty("activeDirectory").ValueKind);
    }
}
