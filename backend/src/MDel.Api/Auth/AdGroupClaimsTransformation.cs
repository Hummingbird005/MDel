using System.Security.Claims;
using MDel.Api.Setup;
using MDel.Application.Abstractions;
using MDel.Application.Common;
using MDel.Domain;
using MDel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace MDel.Api.Auth;

/// <summary>
/// Mapează grupurile AD pe rolurile aplicației (Admin / Approver) conform setărilor
/// Groups.Admins / Groups.Approvers din ecranul de administrare (plus conturile din
/// Setup:InitialAdmins, care sunt mereu administratori) și asigură existența
/// utilizatorului în AppUsers. Rezultatul se cache-uiește 5 minute per utilizator.
/// </summary>
public sealed class AdGroupClaimsTransformation(
    IServiceProvider services,
    ISettingsService settings,
    IConfiguration configuration,
    IMemoryCache cache) : IClaimsTransformation
{
    public const string GroupClaimType = "mdel:group";
    public const string UserIdClaimType = "mdel:user_id";
    private const string TransformedMarker = "mdel:transformed";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not { IsAuthenticated: true, Name: { } loginName }
            || principal.HasClaim(c => c.Type == TransformedMarker))
        {
            return principal;
        }

        // Cheia include grupurile simulate (Development), ca schimbarea lor să nu returneze roluri vechi.
        var devGroups = string.Join(",", principal.FindAll(GroupClaimType).Select(c => c.Value).Order());
        var cacheKey = $"mdel.principal:{loginName.ToLowerInvariant()}|{devGroups}";
        var cached = await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await BuildAsync(principal, loginName);
        });

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(TransformedMarker, "1"));
        identity.AddClaim(new Claim(UserIdClaimType, cached!.UserId.ToString()));
        foreach (var role in cached.Roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        principal.AddIdentity(identity);
        return principal;
    }

    private async Task<CachedPrincipal> BuildAsync(ClaimsPrincipal principal, string loginName)
    {
        var roles = new List<string>();
        var initialAdmin = InitialAdmins.Contains(configuration, loginName);
        if (initialAdmin || await IsMemberAsync(principal, SettingKeys.GroupAdmins, "Authentication:Groups:Admins"))
            roles.Add(AppRoles.Admin);
        if (await IsMemberAsync(principal, SettingKeys.GroupApprovers, "Authentication:Groups:Approvers"))
            roles.Add(AppRoles.Approver);

        using var scope = services.CreateScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<UserProvisioningService>();
        var user = await provisioning.EnsureUserAsync(loginName, isLogin: true, CancellationToken.None);

        // Prima cerere dintr-o fereastră de 5 minute = „sesiune”; o auditam ca login.
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await audit.WriteAsync(new AuditEntry(AuditActions.Login, "AppUser", user.Id.ToString(),
            NewValues: initialAdmin ? new { roles, initialAdmin } : (object)new { roles }, UserIdOverride: user.Id, UserNameOverride: loginName));

        return new CachedPrincipal(user.Id, roles);
    }

    /// <summary>
    /// Grupul se ia din AppSettings (administrare) sau, dacă e gol, din appsettings.json
    /// (bootstrap: primul admin trebuie să poată intra ca să configureze aplicația).
    /// </summary>
    private async Task<bool> IsMemberAsync(ClaimsPrincipal principal, string settingKey, string configKey)
    {
        var group = await settings.GetAsync(settingKey);
        if (string.IsNullOrWhiteSpace(group)) group = configuration[configKey];
        if (string.IsNullOrWhiteSpace(group)) return false;

        var shortName = group.Contains('\\') ? group[(group.IndexOf('\\') + 1)..] : group;

        // Windows/Negotiate: WindowsPrincipal.IsInRole rezolvă grupurile AD (inclusiv imbricate).
        // Development / LDAP: grupurile sunt claim-uri.
        return principal.IsInRole(group)
            || principal.IsInRole(shortName)
            || principal.Claims.Any(c => c.Type == GroupClaimType &&
                   (c.Value.Equals(group, StringComparison.OrdinalIgnoreCase) ||
                    c.Value.Equals(shortName, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record CachedPrincipal(int UserId, IReadOnlyList<string> Roles);
}
