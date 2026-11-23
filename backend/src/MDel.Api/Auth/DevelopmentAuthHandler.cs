using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MDel.Api.Auth;

/// <summary>
/// Autentificare DOAR pentru dezvoltare (fără domeniu AD): autentifică fiecare cerere
/// ca utilizatorul din "Authentication:Development". Poate fi suprascris cu headerele X-Dev-User / X-Dev-Groups.
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

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var section = configuration.GetSection("Authentication:Development");
        var user = Request.Headers["X-Dev-User"].FirstOrDefault() ?? section["User"] ?? "CONTOSO\\dev.admin";
        // X-Dev-Groups: listă separată prin virgulă; "none" = fără grupuri (testare utilizator fără rol).
        var groupsHeader = Request.Headers["X-Dev-Groups"].FirstOrDefault();
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
