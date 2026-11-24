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
/// Mapează grupurile AD pe rolurile aplicației (Admin / Approver / SystemAdmin) conform setărilor
/// Groups.* din ecranul Setări. Conturile din Setup:InitialAdmins primesc fiecare rol cât timp grupul
/// acelui rol nu e configurat (nici în Setări, nici în appsettings.json). Asigură și existența
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
        var initialAdmin = InitialAdmins.Contains(configuration, loginName);
        var bootstrapRoles = new List<string>();
        var roles = new List<string>();
        foreach (var (role, settingKey, configKey) in RoleGroups)
        {
            var group = await GetGroupAsync(settingKey, configKey);
            if (string.IsNullOrWhiteSpace(group))
            {
                // Rolul nu are încă un grup configurat: adminul inițial (appsettings) îl primește,
                // ca să poată configura aplicația. Imediat ce grupul e completat, rolul vine doar din el.
                if (initialAdmin)
                {
                    roles.Add(role);
                    bootstrapRoles.Add(role);
                }
            }
            else if (IsMember(principal, group))
            {
                roles.Add(role);
            }
        }

        using var scope = services.CreateScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<UserProvisioningService>();
        var user = await provisioning.EnsureUserAsync(loginName, isLogin: true, CancellationToken.None);

        // Prima cerere dintr-o fereastră de 5 minute = „sesiune”; o auditam ca login.
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await audit.WriteAsync(new AuditEntry(AuditActions.Login, "AppUser", user.Id.ToString(),
            NewValues: bootstrapRoles.Count > 0 ? new { roles, initialAdminRoles = bootstrapRoles } : new { roles },
            UserIdOverride: user.Id, UserNameOverride: loginName));

        return new CachedPrincipal(user.Id, roles);
    }

    /// <summary>Cele trei roluri și grupurile lor: din Setări sau, dacă sunt goale, din appsettings.json (Authentication:Groups).</summary>
    private static readonly (string Role, string SettingKey, string ConfigKey)[] RoleGroups =
    [
        (AppRoles.Admin, SettingKeys.GroupAdmins, "Authentication:Groups:Admins"),
        (AppRoles.Approver, SettingKeys.GroupApprovers, "Authentication:Groups:Approvers"),
        (AppRoles.SystemAdmin, SettingKeys.GroupSystemAdmins, "Authentication:Groups:SystemAdmins"),
    ];

    private async Task<string?> GetGroupAsync(string settingKey, string configKey)
    {
        var group = await settings.GetAsync(settingKey);
        return string.IsNullOrWhiteSpace(group) ? configuration[configKey] : group;
    }

    private static bool IsMember(ClaimsPrincipal principal, string group)
    {
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
