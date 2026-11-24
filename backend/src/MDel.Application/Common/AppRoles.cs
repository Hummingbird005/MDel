namespace MDel.Application.Common;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Approver = "Approver";

    /// <summary>Administrator de sistem: meniurile Setări și Istoric / Audit.</summary>
    public const string SystemAdmin = "SystemAdmin";

    public static readonly string[] All = [Admin, Approver, SystemAdmin];
}

public static class AppPolicies
{
    /// <summary>Administratori sau aprobatori – pot vedea delegările.</summary>
    public const string Viewer = "Viewer";

    /// <summary>Căutarea de utilizatori în AD: formularul de delegare (Admin) și grupul de informare din Setări (SystemAdmin).</summary>
    public const string DirectorySearch = "DirectorySearch";
}

public static class AuditActions
{
    public const string Login = "Auth.Login";
    public const string HttpRequest = "Http.Request";
    public const string LanguageChanged = "User.LanguageChanged";
}
