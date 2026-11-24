using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MDel.Api.Auth;

/// <summary>
/// Autentificare DOAR pentru dezvoltare (fără domeniu AD): autentifică fiecare cerere
/// ca utilizatorul din "Authentication:Development". Poate fi suprascris cu headerele X-Dev-User / X-Dev-Groups
/// sau, din browser, cu /api/dev/login-as?user=CORP\\ion.popescu&amp;groups=CORP\\MDel-Approvers (cookie).
/// Este refuzată la pornire în afara mediului Development.
/// </summary>
public sealed class DevelopmentAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";
    public const string UserCookie = "mdel-dev-user";
    public const string GroupsCookie = "mdel-dev-groups";

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var section = configuration.GetSection("Authentication:Development");
        // Ordinea: header (teste) → cookie (/api/dev/login-as, din browser) → appsettings.
        var user = Request.Headers["X-Dev-User"].FirstOrDefault()
                   ?? NonEmpty(Request.Cookies[UserCookie])
                   ?? section["User"] ?? "CONTOSO\\dev.admin";
        // X-Dev-Groups: listă separată prin virgulă; "none" = fără grupuri (testare utilizator fără rol).
        var groupsHeader = Request.Headers["X-Dev-Groups"].FirstOrDefault() ?? NonEmpty(Request.Cookies[GroupsCookie]);
        var groups = groupsHeader is null
            ? section.GetSection("Groups").Get<string[]>() ?? []
            : groupsHeader.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? []
                : groupsHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var claims = new List<Claim> { new(ClaimTypes.Name, user) };
        // Simulăm membership-ul AD: grupurile devin claim-uri de tip GroupName,
        // apoi AdGroupClaimsTransformation le mapează pe roluri, exact ca în producție.
        claims.AddRange(groups.Select(g => new Claim(AdGroupClaimsTransformation.GroupClaimType, g)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
