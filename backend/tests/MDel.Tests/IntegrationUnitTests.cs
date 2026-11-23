using MDel.Domain;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Settings;

namespace MDel.Tests;

public class IntegrationUnitTests
{
    [Theory]
    [InlineData("ion", "ion")]
    [InlineData("a*b", @"a\2ab")]
    [InlineData("x)(objectClass=*", @"x\29\28objectClass=\2a")]
    [InlineData(@"dom\user", @"dom\5cuser")]
    public void Ldap_filter_values_are_escaped(string input, string expected) =>
        Assert.Equal(expected, LdapDirectoryService.EscapeFilterValue(input));

    [Fact]
    public void Domain_is_converted_to_base_dn() =>
        Assert.Equal("DC=contoso,DC=local", LdapDirectoryService.DomainToBaseDn("contoso.local"));

    [Theory]
    [InlineData("ManagementObjectNotFoundException", null, true)]
    [InlineData(null, "[Server=EX01] 1A2B,Microsoft.Exchange.Management.RecipientTasks.NotFound", true)]
    [InlineData("ArgumentException", "x", false)]
    public void Exchange_not_found_errors_are_detected(string? reason, string? errorId, bool expected) =>
        Assert.Equal(expected, new ExchangeCommandException("Get-Recipient", "msg", reason, errorId).IsNotFound);

    [Theory]
    [InlineData(SettingKeys.AttachmentsMaxSizeMb, "100", true)]
    [InlineData(SettingKeys.AttachmentsMaxSizeMb, "105", true)]
    [InlineData(SettingKeys.AttachmentsMaxSizeMb, "106", false)]
    [InlineData(SettingKeys.RetentionAuditDays, "365", true)]
    [InlineData(SettingKeys.RetentionAuditDays, "3651", false)]
    [InlineData(SettingKeys.ExchangeKeepCopyInSource, "false", true)]
    [InlineData(SettingKeys.ExchangeKeepCopyInSource, "yes", false)]
    [InlineData(SettingKeys.DefaultLanguage, "en", true)]
    [InlineData(SettingKeys.DefaultLanguage, "de", false)]
    [InlineData(SettingKeys.AttachmentsAllowedExtensions, ".pdf, .docx,.msg", true)]
    [InlineData(SettingKeys.AttachmentsAllowedExtensions, "pdf;exe", false)]
    [InlineData(SettingKeys.ExchangePowerShellUri, "http://ex01.contoso.local/PowerShell/", true)]
    [InlineData(SettingKeys.SmtpFromAddress, "mdel@contoso.local", true)]
    [InlineData(SettingKeys.SmtpFromAddress, "not an email", false)]
    [InlineData(SettingKeys.AdLdapServer, "", true)]
    public void Settings_validator(string key, string value, bool valid)
    {
        var validator = new SettingsValidator(110L * 1024 * 1024);
        Assert.Equal(valid, validator.Validate(key, value) is null);
    }
}
