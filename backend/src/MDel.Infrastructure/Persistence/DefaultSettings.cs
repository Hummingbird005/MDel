using MDel.Domain;
using MDel.Domain.Entities;
using MDel.Infrastructure.Notifications;

namespace MDel.Infrastructure.Persistence;

/// <summary>Valorile implicite inserate în AppSettings prin migrare (HasData).</summary>
internal static class DefaultSettings
{
    // HasData cere valori constante – folosim o dată fixă.
    private static readonly DateTime SeedDate = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    public static AppSetting[] All =>
    [
        S(SettingKeys.AdDomain, "", "ActiveDirectory"),
        S(SettingKeys.AdLdapServer, "", "ActiveDirectory"),
        S(SettingKeys.AdBaseDn, "", "ActiveDirectory"),
        S(SettingKeys.AdServiceUser, "", "ActiveDirectory"),
        S(SettingKeys.AdServicePassword, null, "ActiveDirectory", secret: true),

        S(SettingKeys.GroupAdmins, "", "Groups"),
        S(SettingKeys.GroupApprovers, "", "Groups"),
        S(SettingKeys.GroupSystemAdmins, "", "Groups"),
        S(SettingKeys.GroupAdminsEmail, "", "Groups"),
        S(SettingKeys.GroupApproversEmail, "", "Groups"),

        S(SettingKeys.ExchangePowerShellUri, "", "Exchange"),
        S(SettingKeys.ExchangeServiceUser, "", "Exchange"),
        S(SettingKeys.ExchangeServicePassword, null, "Exchange", secret: true),
        S(SettingKeys.ExchangeKeepCopyInSource, "true", "Exchange"),

        S(SettingKeys.SmtpHost, "", "Smtp"),
        S(SettingKeys.SmtpPort, "25", "Smtp"),
        S(SettingKeys.SmtpUseTls, "true", "Smtp"),
        S(SettingKeys.SmtpUser, "", "Smtp"),
        S(SettingKeys.SmtpPassword, null, "Smtp", secret: true),
        S(SettingKeys.SmtpFromAddress, "", "Smtp"),

        S(SettingKeys.AttachmentsStoragePath, "", "Attachments"),
        S(SettingKeys.AttachmentsMaxSizeMb, "100", "Attachments"),
        S(SettingKeys.AttachmentsAllowedExtensions, ".pdf,.doc,.docx,.xls,.xlsx,.msg,.eml,.txt,.png,.jpg,.jpeg,.zip", "Attachments"),

        S(SettingKeys.RetentionAttachmentsDays, "365", "Retention"),
        S(SettingKeys.RetentionAuditDays, "365", "Retention"),

        S(SettingKeys.DefaultLanguage, "ro", "General"),
        S(SettingKeys.PreventSelfApproval, "true", "General"),
        S(SettingKeys.AppBaseUrl, "", "General"),
        .. TemplateSettings(),
    ];

    /// <summary>Șabloanele de email în română, cu textul implicit (editabile din Setări → Șabloane email).</summary>
    private static IEnumerable<AppSetting> TemplateSettings() =>
        Enum.GetValues<TemplateKind>().SelectMany(kind => new[]
        {
            S(NotificationTemplates.SubjectKey(kind), NotificationTemplates.RomanianDefaults[kind].Subject, "Templates"),
            S(NotificationTemplates.BodyKey(kind), NotificationTemplates.RomanianDefaults[kind].Body, "Templates"),
        });

    private static AppSetting S(string key, string? value, string category, bool secret = false) => new()
    {
        Key = key,
        Value = value,
        Category = category,
        IsSecret = secret,
        UpdatedUtc = SeedDate,
    };
}
