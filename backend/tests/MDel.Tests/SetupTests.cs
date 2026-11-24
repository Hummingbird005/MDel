using System.Net.Http.Json;
using MDel.Application.Abstractions;
using MDel.Application.Common;
using MDel.Domain;
using MDel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MDel.Tests;

/// <summary>Prima pornire: setări inițiale din appsettings, admin inițial, login cu AD neconfigurat.</summary>
public class SetupTests
{
    private sealed record MeDto(string SamAccountName, string DisplayName, string[] Roles);

    private sealed class SetupFactory(
        InMemoryDatabaseRoot root,
        string dbName,
        IReadOnlyDictionary<string, string> config,
        IDirectoryService? directory = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Scheduler:Enabled", "false");
            foreach (var (key, value) in config) builder.UseSetting(key, value);
            builder.ConfigureTestServices(services =>
            {
                foreach (var d in services.Where(d =>
                             d.ServiceType == typeof(DbContextOptions<MDelDbContext>) ||
                             d.ServiceType == typeof(IDbContextOptionsConfiguration<MDelDbContext>)).ToList())
                {
                    services.Remove(d);
                }

                services.AddDbContext<MDelDbContext>(o => o.UseInMemoryDatabase(dbName, root));
                if (directory is not null) services.Replace(ServiceDescriptor.Singleton(directory));
            });
        }

        public async Task<string?> SettingAsync(string key)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MDelDbContext>().AppSettings
                .Where(s => s.Key == key).Select(s => s.Value).SingleAsync();
        }
    }

    [Fact]
    public async Task Initial_settings_fill_empty_values_only_and_never_overwrite_saved_ones()
    {
        var root = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString();
        var config = new Dictionary<string, string>
        {
            ["Setup:InitialSettings:Ad.Domain"] = "contoso.local",
            ["Setup:InitialSettings:Groups.Admins"] = "CONTOSO\\MDel-Admins",
            ["Setup:InitialSettings:General.BaseUrl"] = "",
        };

        await using (var first = new SetupFactory(root, dbName, config))
        {
            first.CreateClient();
            Assert.Equal("contoso.local", await first.SettingAsync(SettingKeys.AdDomain));
            Assert.Equal("CONTOSO\\MDel-Admins", await first.SettingAsync(SettingKeys.GroupAdmins));

            // Administratorul schimbă domeniul din ecranul Setări.
            using var scope = first.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISettingsService>()
                .UpdateAsync(new Dictionary<string, string?> { [SettingKeys.AdDomain] = "corp.contoso.local" });
        }

        // Repornire cu același appsettings: valoarea salvată din Setări rămâne.
        await using var second = new SetupFactory(root, dbName, config);
        second.CreateClient();
        Assert.Equal("corp.contoso.local", await second.SettingAsync(SettingKeys.AdDomain));
    }

    [Fact]
    public async Task Invalid_initial_settings_stop_the_startup()
    {
        var config = new Dictionary<string, string> { ["Setup:InitialSettings:Groups.AdminsEmail"] = "not-an-email" };
        await using var factory = new SetupFactory(new InMemoryDatabaseRoot(), Guid.NewGuid().ToString(), config);

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Groups.AdminsEmail", ex.ToString());
    }

    [Theory]
    [InlineData("CONTOSO\\boot.admin")]
    [InlineData("boot.admin")]
    public async Task Initial_admin_is_administrator_without_any_ad_group(string configured)
    {
        var config = new Dictionary<string, string> { ["Setup:InitialAdmins:0"] = configured };
        await using var factory = new SetupFactory(new InMemoryDatabaseRoot(), Guid.NewGuid().ToString(), config);

        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\boot.admin");
        admin.DefaultRequestHeaders.Add("X-Dev-Groups", "none");
        var me = await admin.GetFromJsonAsync<MeDto>("/api/me");
        Assert.Contains(AppRoles.Admin, me!.Roles);
        Assert.DoesNotContain(AppRoles.Approver, me.Roles);

        var settings = await admin.GetAsync("/api/admin/settings");
        Assert.True(settings.IsSuccessStatusCode);

        // Alt utilizator fără grupuri rămâne fără drepturi.
        var other = factory.CreateClient();
        other.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\ion.popescu");
        other.DefaultRequestHeaders.Add("X-Dev-Groups", "none");
        var otherMe = await other.GetFromJsonAsync<MeDto>("/api/me");
        Assert.Empty(otherMe!.Roles);
    }

    [Fact]
    public async Task Login_works_while_ad_is_not_configured_yet()
    {
        await using var factory = new SetupFactory(new InMemoryDatabaseRoot(), Guid.NewGuid().ToString(),
            new Dictionary<string, string> { ["Setup:InitialAdmins:0"] = "CONTOSO\\boot.admin" },
            new UnconfiguredDirectory());

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\boot.admin");
        client.DefaultRequestHeaders.Add("X-Dev-Groups", "none");
        var me = await client.GetFromJsonAsync<MeDto>("/api/me");

        Assert.Equal("boot.admin", me!.SamAccountName);
        Assert.Equal("boot.admin", me.DisplayName);
        Assert.Contains(AppRoles.Admin, me.Roles);
    }

    /// <summary>Ca LdapDirectoryService înainte de completarea secțiunii AD din Setări.</summary>
    private sealed class UnconfiguredDirectory : IDirectoryService
    {
        private static InvalidOperationException NotConfigured() =>
            new("AD is not configured: set the LDAP server or the domain in Administration.");

        public Task<IReadOnlyList<DirectoryUser>> SearchUsersAsync(string term, int maxResults, CancellationToken ct) =>
            throw NotConfigured();

        public Task<DirectoryUser?> FindBySamAccountNameAsync(string samAccountName, CancellationToken ct) =>
            throw NotConfigured();

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct) =>
            Task.FromResult(new ConnectionTestResult(false, "not configured"));
    }
}
