using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MDel.Application.Abstractions;
using MDel.Domain;
using MDel.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Tests;

public class AdminApiTests(MDelApiFactory factory) : IClassFixture<MDelApiFactory>
{
    private sealed record SettingDto(string Key, string Category, string? Value, bool IsSecret, bool HasValue);
    private sealed record SettingsDto(SettingDto[] Settings, int MaxAttachmentMb);
    private sealed record TestResultDto(bool Success, string Message);
    private sealed record UserDto(string SamAccountName, string DisplayName, string? Email, bool HasMailbox);

    private HttpClient AdminClient() => factory.CreateClient();

    private HttpClient NoRoleClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "CONTOSO\\ion.popescu");
        client.DefaultRequestHeaders.Add("X-Dev-Groups", "none");
        return client;
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, Dictionary<string, string?> values) =>
        client.PutAsJsonAsync("/api/admin/settings", new { values });

    [Fact]
    public async Task Settings_are_listed_with_defaults_and_secrets_hidden()
    {
        var dto = await AdminClient().GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Assert.NotNull(dto);
        Assert.Equal(105, dto.MaxAttachmentMb); // 110 MB limită server - 5 MB marjă
        Assert.Contains(dto.Settings, s => s.Key == SettingKeys.RetentionAuditDays && s.Value == "365");
        Assert.All(dto.Settings.Where(s => s.IsSecret), s => Assert.Null(s.Value));
    }

    [Fact]
    public async Task Secret_is_encrypted_never_returned_and_decryptable_by_the_service()
    {
        var response = await Put(AdminClient(), new() { [SettingKeys.SmtpPassword] = "S3cret!" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var dto = await AdminClient().GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        var secret = dto!.Settings.Single(s => s.Key == SettingKeys.SmtpPassword);
        Assert.True(secret.HasValue);
        Assert.Null(secret.Value);

        var stored = factory.WithDb(db => db.AppSettings.Single(s => s.Key == SettingKeys.SmtpPassword).Value);
        Assert.NotEqual("S3cret!", stored);

        var settings = factory.Services.GetRequiredService<ISettingsService>();
        Assert.Equal("S3cret!", await settings.GetSecretAsync(SettingKeys.SmtpPassword));

        // null = neschimbat
        await Put(AdminClient(), new() { [SettingKeys.SmtpPassword] = null });
        Assert.Equal("S3cret!", await settings.GetSecretAsync(SettingKeys.SmtpPassword));

        // Auditul nu conține parola în clar.
        var audit = factory.WithDb(db => db.AuditLogs.Where(a => a.Action == "Settings.Updated")
            .Select(a => a.NewValues).ToList());
        Assert.DoesNotContain(audit, v => v != null && v.Contains("S3cret!"));
    }

    [Fact]
    public async Task Updating_settings_is_audited_with_old_and_new_values()
    {
        var response = await Put(AdminClient(), new() { [SettingKeys.RetentionAttachmentsDays] = "730" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var settings = factory.Services.GetRequiredService<ISettingsService>();
        Assert.Equal("730", await settings.GetAsync(SettingKeys.RetentionAttachmentsDays));

        var entry = factory.WithDb(db => db.AuditLogs
            .Where(a => a.Action == "Settings.Updated" && a.EntityId!.Contains(SettingKeys.RetentionAttachmentsDays))
            .OrderByDescending(a => a.Id).First());
        Assert.Contains("365", entry.OldValues);
        Assert.Contains("730", entry.NewValues);
    }

    [Theory]
    [InlineData(SettingKeys.AttachmentsMaxSizeMb, "500")]
    [InlineData(SettingKeys.AttachmentsMaxSizeMb, "abc")]
    [InlineData(SettingKeys.RetentionAuditDays, "0")]
    [InlineData(SettingKeys.SmtpPort, "70000")]
    [InlineData(SettingKeys.AttachmentsStoragePath, "relative/path")]
    [InlineData(SettingKeys.ExchangePowerShellUri, "not a url")]
    [InlineData("Unknown.Key", "x")]
    public async Task Invalid_settings_are_rejected_per_key(string key, string value)
    {
        var response = await Put(AdminClient(), new() { [key] = value });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty(key, out _));
    }

    [Fact]
    public async Task Non_admin_is_forbidden_and_the_attempt_is_audited()
    {
        var response = await NoRoleClient().GetAsync("/api/admin/settings");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var denied = factory.WithDb(db => db.AuditLogs.Any(a =>
            a.UserName == "CONTOSO\\ion.popescu" && a.Result == AuditResult.Denied && a.Path == "/api/admin/settings"));
        Assert.True(denied);
    }

    [Fact]
    public async Task Non_admin_has_no_roles()
    {
        var me = await NoRoleClient().GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal(0, me.GetProperty("roles").GetArrayLength());
    }

    [Theory]
    [InlineData("ad")]
    [InlineData("exchange")]
    [InlineData("smtp")]
    public async Task Connection_tests_run_against_fakes_and_are_audited(string target)
    {
        var response = await AdminClient().PostAsJsonAsync($"/api/admin/test/{target}", new { });
        var result = await response.Content.ReadFromJsonAsync<TestResultDto>();

        Assert.True(result!.Success, result.Message);
        Assert.True(factory.WithDb(db => db.AuditLogs.Any(a => a.Action.StartsWith("Admin.Test."))));
    }

    [Fact]
    public async Task Storage_test_reports_missing_path_and_succeeds_with_a_writable_folder()
    {
        var client = AdminClient();
        await Put(client, new() { [SettingKeys.AttachmentsStoragePath] = "" });
        var missing = await (await client.PostAsync("/api/admin/test/storage", null)).Content.ReadFromJsonAsync<TestResultDto>();
        Assert.False(missing!.Success);

        var folder = Path.Combine(Path.GetTempPath(), "mdel-tests", Guid.NewGuid().ToString("N"));
        await Put(client, new() { [SettingKeys.AttachmentsStoragePath] = folder });
        var ok = await (await client.PostAsync("/api/admin/test/storage", null)).Content.ReadFromJsonAsync<TestResultDto>();

        Assert.True(ok!.Success, ok.Message);
        Assert.Empty(Directory.GetFiles(folder)); // fișierul de probă a fost șters
    }

    [Fact]
    public async Task Directory_search_returns_only_mailbox_users_by_default()
    {
        var users = await AdminClient().GetFromJsonAsync<UserDto[]>("/api/directory/users?q=s");
        Assert.Empty(users!); // minim 2 caractere

        var all = await AdminClient().GetFromJsonAsync<UserDto[]>("/api/directory/users?q=ser&mailboxOnly=false");
        var mailboxes = await AdminClient().GetFromJsonAsync<UserDto[]>("/api/directory/users?q=ser");
        Assert.Contains(all!, u => u.SamAccountName == "svc.nomailbox");
        Assert.DoesNotContain(mailboxes!, u => u.SamAccountName == "svc.nomailbox");
    }
}
