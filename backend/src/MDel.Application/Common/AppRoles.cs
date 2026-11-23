namespace MDel.Application.Common;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Approver = "Approver";
}

public static class AuditActions
{
    public const string Login = "Auth.Login";
    public const string HttpRequest = "Http.Request";
    public const string LanguageChanged = "User.LanguageChanged";
}
