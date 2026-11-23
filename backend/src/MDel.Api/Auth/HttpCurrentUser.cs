using MDel.Application.Abstractions;
using MDel.Api.Middleware;

namespace MDel.Api.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private HttpContext? Context => accessor.HttpContext;

    public bool IsAuthenticated => Context?.User.Identity?.IsAuthenticated ?? false;

    public int? UserId =>
        int.TryParse(Context?.User.FindFirst(AdGroupClaimsTransformation.UserIdClaimType)?.Value, out var id) ? id : null;

    public string UserName => IsAuthenticated ? Context!.User.Identity!.Name ?? "unknown" : Context is null ? "system" : "anonymous";

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => Context?.Items[CorrelationIdMiddleware.ItemKey] as string;

    public bool IsInRole(string role) => Context?.User.IsInRole(role) ?? false;
}
