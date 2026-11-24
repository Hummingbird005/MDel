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
    public void Fakes_are_used_by_default_in_development() =>
        Assert.IsType<FakeDirectoryService>(factory.Services.GetRequiredService<IDirectoryService>());

    [Fact]
    public void Enabled_active_directory_section_uses_real_ldap_while_exchange_and_mail_stay_fake()
    {
        using var configured = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ActiveDirectory:Enabled", "true");
            b.UseSetting("ActiveDirectory:Server", "dc01.corp.local");
        });

        Assert.IsType<LdapDirectoryService>(configured.Services.GetRequiredService<IDirectoryService>());
        Assert.IsType<FakeExchangeService>(configured.Services.GetRequiredService<IExchangeService>());
        Assert.IsType<FakeMailService>(configured.Services.GetRequiredService<IMailService>());
    }

    [Fact]
    public void Each_integration_can_be_switched_to_real()
    {
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting("Integrations:FakeExchange", "false"));
        Assert.IsType<ExchangePowerShellService>(configured.Services.GetRequiredService<IExchangeService>());
        Assert.IsType<FakeDirectoryService>(configured.Services.GetRequiredService<IDirectoryService>());
    }
}
