using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Http.Json;
using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Tests;

/// <summary>Conexiunea la AD configurată strict din Setări; conturile și parolele criptate în baza de date.</summary>
public class ActiveDirectoryConfigTests(MDelApiFactory factory) : IClassFixture<MDelApiFactory>
{
    private static LdapConnectionInfo Info(string? server = "dc01.corp.local", string? domain = "corp.local", string? port = "636",
        bool ssl = true, string? bases = null, string? user = null, string? password = null) =>
        LdapConnectionInfo.FromSettings(server, domain, port, ssl, true, bases, user, password);

    [Fact]
    public void Ldaps_with_dn_account_and_several_search_ous()
    {
        var info = Info(bases: "OU=Angajati,DC=corp,DC=local\r\n\nOU=Externi,DC=corp,DC=local\nou=angajati,dc=corp,dc=local ",
            user: "CN=svc-mdel,OU=Servicii,DC=corp,DC=local", password: "x");

        Assert.Equal(("dc01.corp.local", 636, true), (info.Host, info.Port, info.UseSsl));
        Assert.Equal(AuthType.Basic, info.AuthType); // un DN se poate folosi doar cu simple bind
        Assert.Equal(["OU=Angajati,DC=corp,DC=local", "OU=Externi,DC=corp,DC=local"], info.SearchBases); // fără goale și duplicate
    }

    [Theory]
    [InlineData("CORP\\svc-mdel", AuthType.Negotiate)]
    [InlineData("svc-mdel@corp.local", AuthType.Negotiate)]
    [InlineData("CN=svc,DC=corp,DC=local", AuthType.Basic)]
    [InlineData(null, AuthType.Negotiate)]
    public void Auth_type_is_chosen_from_the_account_format(string? user, AuthType expected) =>
        Assert.Equal(expected, LdapConnectionInfo.ResolveAuthType(null, user));

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
    public void The_port_field_overrides_the_default_and_empty_keeps_it()
    {
        Assert.Equal(3269, Info(port: "3269").Port);
        Assert.Equal(636, Info(port: "").Port);
        Assert.Equal(389, Info(port: null, ssl: false).Port);
    }

    [Fact]
    public void Simple_bind_without_ssl_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Info(ssl: false, port: null, user: "CN=svc,DC=corp,DC=local", password: "x"));
        Assert.Contains("Use SSL", ex.Message);
    }

    [Fact]
    public void Missing_server_and_domain_is_reported()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Info(server: "", domain: null));
        Assert.Contains("Settings → Active Directory", ex.Message);
    }

    [Fact]
    public void The_domain_is_used_when_no_server_is_given()
    {
        var info = Info(server: "", domain: "corp.local", ssl: false, port: null);
        Assert.Equal(("corp.local", 389), (info.Host, info.Port));
    }

    [Theory]
    [InlineData(49, "80090308: LdapErr: DSID-0C0903A9, comment: AcceptSecurityContext error, data 52e, v1db1", "wrong service account or password")]
    [InlineData(49, "80090308: LdapErr: DSID-0C0903A9, comment: AcceptSecurityContext error, data 775, v1db1", "locked out")]
    [InlineData(81, null, "over SSL")]
    [InlineData(8, null, "Use SSL")]
    public void Ldap_errors_are_explained(int code, string? serverMessage, string expected)
    {
        var message = LdapDirectoryService.Explain(new LdapException(code, "LDAP error", serverMessage), Info());
        Assert.Contains(expected, message);
        Assert.Contains($"(LDAP {code})", message);
    }

    // ------------------------------------------------------------------ Secrete criptate

    private sealed record SettingsDto(List<SettingDto> Settings);

    private sealed record SettingDto(string Key, string? Value, bool IsSecret, bool HasValue);

    [Fact]
    public async Task Accounts_and_passwords_are_secrets_encrypted_in_the_database_and_never_returned()
    {
        var client = factory.CreateClient();
        var save = await client.PutAsJsonAsync("/api/admin/settings", new
        {
            values = new Dictionary<string, string?>
            {
                [SettingKeys.AdServiceUser] = "CORP\\svc-ad-secret",
                [SettingKeys.AdServicePassword] = "Parola-AD-secret",
                [SettingKeys.ExchangeServiceUser] = "CORP\\svc-ex-secret",
                [SettingKeys.SmtpUser] = "svc-smtp-secret",
            },
        });
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);

        // API-ul nu întoarce valorile, doar că sunt setate.
        var raw = await client.GetStringAsync("/api/admin/settings");
        Assert.DoesNotContain("svc-ad-secret", raw);
        Assert.DoesNotContain("Parola-AD-secret", raw);
        var dto = await client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        foreach (var key in new[] { SettingKeys.AdServiceUser, SettingKeys.AdServicePassword, SettingKeys.ExchangeServiceUser, SettingKeys.SmtpUser })
        {
            var s = dto!.Settings.Single(x => x.Key == key);
            Assert.True(s is { IsSecret: true, HasValue: true, Value: null }, key);
        }

        // În baza de date sunt criptate, iar serviciile le citesc decriptate.
        var stored = factory.WithDb(db => db.AppSettings.Where(s => s.Key == SettingKeys.AdServiceUser || s.Key == SettingKeys.AdServicePassword)
            .Select(s => s.Value).ToList());
        Assert.All(stored, v => Assert.StartsWith("CfDJ8", v));
        Assert.DoesNotContain(stored, v => v!.Contains("svc-ad-secret") || v.Contains("Parola-AD-secret"));
        var settings = factory.Services.GetRequiredService<ISettingsService>();
        Assert.Equal("CORP\\svc-ad-secret", await settings.GetSecretAsync(SettingKeys.AdServiceUser));
        Assert.Equal("svc-smtp-secret", await settings.GetSecretAsync(SettingKeys.SmtpUser));

        // Nici auditul nu păstrează valorile.
        var audit = factory.WithDb(db => db.AuditLogs.Where(a => a.Action == "Settings.Updated")
            .Select(a => (a.OldValues ?? "") + (a.NewValues ?? "")).ToList());
        Assert.DoesNotContain(audit, a => a.Contains("svc-ad-secret") || a.Contains("Parola-AD-secret") || a.Contains("svc-smtp-secret"));
    }

    [Fact]
    public async Task Plain_text_values_saved_before_are_encrypted_once_at_startup()
    {
        factory.WithDb(db =>
        {
            var legacy = db.AppSettings.Single(s => s.Key == SettingKeys.ExchangeServicePassword);
            legacy.Value = "parola-veche-necriptata";
            db.SaveChanges();
            return 0;
        });
        var settings = factory.Services.GetRequiredService<ISettingsService>();

        Assert.True(await settings.EncryptPlainSecretsAsync() >= 1);
        Assert.Equal("parola-veche-necriptata", await settings.GetSecretAsync(SettingKeys.ExchangeServicePassword));
        Assert.StartsWith("CfDJ8", factory.WithDb(db => db.AppSettings.Single(s => s.Key == SettingKeys.ExchangeServicePassword).Value));
        Assert.Equal(0, await settings.EncryptPlainSecretsAsync()); // idempotent
    }

    [Theory]
    [InlineData("Ad.Port", "99999")]
    [InlineData("Ad.Port", "abc")]
    [InlineData("Ad.UseSsl", "poate")]
    [InlineData("Ad.SearchBases", "Angajati")]
    public async Task Invalid_ad_settings_are_rejected(string key, string value)
    {
        var response = await factory.CreateClient().PutAsJsonAsync("/api/admin/settings",
            new { values = new Dictionary<string, string?> { [key] = value } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Valid_ad_settings_are_saved_and_the_old_appsettings_section_is_ignored()
    {
        var client = factory.CreateClient();
        var save = await client.PutAsJsonAsync("/api/admin/settings", new
        {
            values = new Dictionary<string, string?>
            {
                [SettingKeys.AdPort] = "636",
                [SettingKeys.AdUseSsl] = "true",
                [SettingKeys.AdSearchBases] = "OU=Angajati,DC=corp,DC=local\nOU=Externi,DC=corp,DC=local",
            },
        });
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
        var dto = await client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        Assert.Contains("OU=Externi", dto!.Settings.Single(s => s.Key == SettingKeys.AdSearchBases).Value);
        Assert.DoesNotContain(dto.Settings, s => s.Key == "Ad.BaseDn");
        // Fără conexiune activă la un DC real, testul raportează eroare, nu excepție.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/admin/test/ad", null)).StatusCode);
    }
}
