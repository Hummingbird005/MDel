namespace MDel.Application.Common;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Approver = "Approver";
}

public static class AppPolicies
{
    /// <summary>Administratori sau aprobatori – pot vedea delegările.</summary>
    public const string Viewer = "Viewer";
}

public static class AuditActions
{
    public const string Login = "Auth.Login";
    public const string HttpRequest = "Http.Request";
    public const string LanguageChanged = "User.LanguageChanged";
}
