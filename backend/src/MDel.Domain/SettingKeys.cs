namespace MDel.Domain;

/// <summary>Cheile setărilor din tabela AppSettings.</summary>
public static class SettingKeys
{
    // Active Directory
    public const string AdDomain = "Ad.Domain";
    public const string AdLdapServer = "Ad.LdapServer";
    public const string AdBaseDn = "Ad.BaseDn";
    public const string AdServiceUser = "Ad.ServiceUser";
    public const string AdServicePassword = "Ad.ServicePassword";

    // Grupuri AD mapate pe roluri
    public const string GroupAdmins = "Groups.Admins";
    public const string GroupApprovers = "Groups.Approvers";
    public const string GroupAdminsEmail = "Groups.AdminsEmail";
    public const string GroupApproversEmail = "Groups.ApproversEmail";

    // Exchange 2016 (PowerShell remoting)
    public const string ExchangePowerShellUri = "Exchange.PowerShellUri";
    public const string ExchangeServiceUser = "Exchange.ServiceUser";
    public const string ExchangeServicePassword = "Exchange.ServicePassword";
    public const string ExchangeKeepCopyInSource = "Exchange.KeepCopyInSource";

    // SMTP
    public const string SmtpHost = "Smtp.Host";
    public const string SmtpPort = "Smtp.Port";
    public const string SmtpUseTls = "Smtp.UseTls";
    public const string SmtpUser = "Smtp.User";
    public const string SmtpPassword = "Smtp.Password";
    public const string SmtpFromAddress = "Smtp.FromAddress";

    // Fișiere
    public const string AttachmentsStoragePath = "Attachments.StoragePath";
    public const string AttachmentsMaxSizeMb = "Attachments.MaxSizeMb";
    public const string AttachmentsAllowedExtensions = "Attachments.AllowedExtensions";

    // Retenție
    public const string RetentionAttachmentsDays = "Retention.AttachmentsDays";
    public const string RetentionAuditDays = "Retention.AuditDays";

    // General
    public const string DefaultLanguage = "General.DefaultLanguage";
    public const string PreventSelfApproval = "General.PreventSelfApproval";

    /// <summary>Adresa publică a aplicației (ex. https://mdel.contoso.local) – pentru link-urile din emailuri.</summary>
    public const string AppBaseUrl = "General.BaseUrl";
}
