using MDel.Domain;

namespace MDel.Infrastructure.Settings;

/// <summary>Reguli de validare pentru valorile introduse în ecranul de administrare.</summary>
public sealed class SettingsValidator(long maxRequestBodyBytes)
{
    private static readonly HashSet<string> BoolKeys =
    [
        SettingKeys.ExchangeKeepCopyInSource, SettingKeys.SmtpUseTls, SettingKeys.PreventSelfApproval,
    ];

    /// <summary>Limita maximă permisă pentru atașamente = limita serverului minus o marjă pentru restul formularului.</summary>
    public int MaxAttachmentMb => (int)(maxRequestBodyBytes / (1024 * 1024)) - 5;

    public string? Validate(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return null; // golirea unei setări e permisă

        if (BoolKeys.Contains(key))
            return bool.TryParse(value, out _) ? null : "Must be 'true' or 'false'.";

        switch (key)
        {
            case SettingKeys.AttachmentsMaxSizeMb:
                return int.TryParse(value, out var mb) && mb >= 1 && mb <= MaxAttachmentMb
                    ? null : $"Must be an integer between 1 and {MaxAttachmentMb}.";

            case SettingKeys.RetentionAttachmentsDays:
            case SettingKeys.RetentionAuditDays:
                return int.TryParse(value, out var days) && days >= 1 && days <= 3650
                    ? null : "Must be an integer between 1 and 3650 days.";

            case SettingKeys.SmtpPort:
                return int.TryParse(value, out var port) && port is >= 1 and <= 65535
                    ? null : "Must be a port between 1 and 65535.";

            case SettingKeys.DefaultLanguage:
                return value is "ro" or "en" ? null : "Must be 'ro' or 'en'.";

            case SettingKeys.AttachmentsStoragePath:
                return Path.IsPathFullyQualified(value) ? null : "Must be an absolute path (e.g. D:\\MDel\\Files or \\\\server\\share\\MDel).";

            case SettingKeys.AttachmentsAllowedExtensions:
                return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .All(e => e.StartsWith('.') && e.Length > 1 && e[1..].All(char.IsLetterOrDigit))
                    ? null : "Must be a comma-separated list like .pdf,.docx";

            case SettingKeys.ExchangePowerShellUri:
                return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                    ? null : "Must be a URL like http://exchange01.contoso.local/PowerShell/";

            case SettingKeys.SmtpFromAddress:
            case SettingKeys.GroupAdminsEmail:
            case SettingKeys.GroupApproversEmail:
                return value.Contains('@') && !value.Contains(' ') ? null : "Must be an email address.";

            default:
                return value.Length > 4000 ? "Value is too long." : null;
        }
    }
}
