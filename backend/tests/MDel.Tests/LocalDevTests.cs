using System.Net;
using System.Net.Http.Json;
using MDel.Application.Abstractions;
using MDel.Application.Common;
using MDel.Infrastructure.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Tests;

/// <summary>Rularea locală (Development): schimbarea utilizatorului din browser și alegerea integrărilor reale.</summary>
public class LocalDevTests(MDelApiFactory factory) : IClassFixture<MDelApiFactory>
{
    private sealed record MeDto(string SamAccountName, string[] Roles);

    [Fact]
    public async Task Login_as_switches_the_simulated_user_until_reset()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        var redirect = await client.GetAsync("/api/dev/login-as?user=dev.approver&groups=MDel-Approvers");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/", redirect.Headers.Location!.OriginalString);

        var me = await client.GetFromJsonAsync<MeDto>("/api/me");
        Assert.Equal("dev.approver", me!.SamAccountName);
        Assert.Equal([AppRoles.Approver], me.Roles);

        await client.GetAsync("/api/dev/login-as?user=CONTOSO%5Cion.popescu&groups=none");
        me = await client.GetFromJsonAsync<MeDto>("/api/me");
        Assert.Equal("ion.popescu", me!.SamAccountName);
        Assert.Empty(me.Roles);

        // Fără parametri: înapoi la utilizatorul din appsettings.Development.json.
        await client.GetAsync("/api/dev/login-as");
        me = await client.GetFromJsonAsync<MeDto>("/api/me");
        Assert.Equal("dev.admin", me!.SamAccountName);
        Assert.Contains(AppRoles.Admin, me.Roles);
    }

    [Fact]
    public void Simulated_directory_is_the_default_in_development() =>
        Assert.IsType<ConfigurableDirectoryService>(factory.Services.GetRequiredService<IDirectoryService>());

    [Fact]
    public async Task Real_ad_is_used_as_soon_as_it_is_filled_in_settings_without_restart()
    {
        var client = factory.CreateClient();
        const string search = "/api/directory/users?q=ion&mailboxOnly=false";

        // Nesetat: AD simulat.
        var simulated = await client.GetFromJsonAsync<List<DirectoryUserDto>>(search);
        Assert.Contains(simulated!, u => u.SamAccountName == "ion.popescu");

        try
        {
            // Completat (un server care nu răspunde): se încearcă AD-ul real și eșuează cu 502.
            await client.PutAsJsonAsync("/api/admin/settings",
                new { values = new Dictionary<string, string?> { ["Ad.LdapServer"] = "127.0.0.1:1" } });
            Assert.Equal(HttpStatusCode.BadGateway, (await client.GetAsync(search)).StatusCode);
        }
        finally
        {
            await client.PutAsJsonAsync("/api/admin/settings", new { values = new Dictionary<string, string?> { ["Ad.LdapServer"] = "" } });
        }

        Assert.Contains((await client.GetFromJsonAsync<List<DirectoryUserDto>>(search))!, u => u.SamAccountName == "ion.popescu");
    }

    private sealed record DirectoryUserDto(string SamAccountName);

    [Theory]
    [InlineData("true", typeof(FakeDirectoryService))]
    [InlineData("false", typeof(LdapDirectoryService))]
    public void The_directory_can_be_forced_simulated_or_real(string fake, Type expected)
    {
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting("Integrations:FakeDirectory", fake));
        Assert.IsType(expected, configured.Services.GetRequiredService<IDirectoryService>());
    }

    [Fact]
    public void Each_integration_can_be_switched_to_real()
    {
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting("Integrations:FakeExchange", "false"));
        Assert.IsType<ExchangePowerShellService>(configured.Services.GetRequiredService<IExchangeService>());
        Assert.IsType<ConfigurableDirectoryService>(configured.Services.GetRequiredService<IDirectoryService>());
    }
}
