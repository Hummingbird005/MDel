using System.Net;
using System.Net.Http.Json;
using MDel.Domain.Entities;
using MDel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MDel.Tests;

public class MDelApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Joburile automate rulează doar când testul le cheamă explicit (cu ceasul de test).
        builder.UseSetting("Scheduler:Enabled", "false");
        builder.ConfigureServices(services =>
        {
            // Înlocuim SQL Server cu o bază in-memory pentru testele de API.
            foreach (var d in services.Where(d =>
                         d.ServiceType == typeof(DbContextOptions<MDelDbContext>) ||
                         d.ServiceType == typeof(IDbContextOptionsConfiguration<MDelDbContext>)).ToList())
            {
                services.Remove(d);
            }

            services.AddDbContext<MDelDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        // Aplică HasData (setările implicite) în baza in-memory.
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<MDelDbContext>().Database.EnsureCreated();
        return host;
    }

    public T WithDb<T>(Func<MDelDbContext, T> action)
    {
        using var scope = Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<MDelDbContext>());
    }
}

public class ApiTests(MDelApiFactory factory) : IClassFixture<MDelApiFactory>
{
    private sealed record MeDto(string SamAccountName, string DisplayName, string? Email, string[] Roles, string PreferredLanguage);

    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_user_with_roles_mapped_from_ad_groups()
    {
        var me = await factory.CreateClient().GetFromJsonAsync<MeDto>("/api/me");

        Assert.NotNull(me);
        Assert.Equal("dev.admin", me.SamAccountName);
        Assert.Equal("Dev Admin", me.DisplayName);
        Assert.Contains("Admin", me.Roles);
        Assert.Contains("Approver", me.Roles);
    }

    [Fact]
    public async Task New_user_is_provisioned_from_directory()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\ion.popescu");
        var me = await client.GetFromJsonAsync<MeDto>("/api/me");

        Assert.Equal("ion.popescu", me!.SamAccountName);
        Assert.Equal("Ion Popescu", me.DisplayName);
    }

    [Fact]
    public async Task Changing_language_is_persisted_and_audited()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\maria.ionescu");

        var response = await client.PutAsJsonAsync("/api/me/language", new { language = "en" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var me = await client.GetFromJsonAsync<MeDto>("/api/me");
        Assert.Equal("en", me!.PreferredLanguage);

        var actions = factory.WithDb(db => db.AuditLogs.Where(a => a.UserName == "CONTOSO\\maria.ionescu")
            .Select(a => a.Action).ToList());
        Assert.Contains("User.LanguageChanged", actions);
        Assert.Contains("Http.Request", actions);
    }

    [Fact]
    public async Task Invalid_language_is_rejected()
    {
        var response = await factory.CreateClient().PutAsJsonAsync("/api/me/language", new { language = "fr" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_api_route_is_404_not_spa()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
