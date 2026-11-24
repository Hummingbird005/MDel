using MDel.Api.Auth;
using MDel.Api.Middleware;
using MDel.Api.Setup;
using MDel.Application.Abstractions;
using MDel.Application.Common;
using MDel.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- Limite upload
// Atașamente de max. 100 MB (configurabil din administrare); limita tehnică a serverului
// trebuie să fie >= setarea din aplicație. Pe IIS se setează și maxAllowedContentLength (web.config).
var maxRequestBytes = builder.Configuration.GetValue<long>("Uploads:MaxRequestBodyBytes", 110L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxRequestBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxRequestBytes);

// ---------------------------------------------------------------- Servicii
builder.Services.AddInfrastructure(builder.Configuration);

// Doar Development: bază in-memory pentru a încerca interfața fără SQL Server.
var useInMemoryDb = builder.Environment.IsDevelopment() && builder.Configuration.GetValue("Database:UseInMemory", false);
if (useInMemoryDb)
{
    builder.Services.RemoveAll<Microsoft.EntityFrameworkCore.DbContextOptions<MDel.Infrastructure.Persistence.MDelDbContext>>();
    builder.Services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<MDel.Infrastructure.Persistence.MDelDbContext>>();
    builder.Services.AddDbContext<MDel.Infrastructure.Persistence.MDelDbContext>(o => o.UseInMemoryDatabase("mdel-dev"));
}
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ---------------------------------------------------------------- Data Protection
// Cheile criptează parolele din AppSettings. Trebuie persistate într-un folder stabil
// (altfel, la reinstalare, parolele salvate nu mai pot fi decriptate și trebuie reintroduse).
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("MDel");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    if (OperatingSystem.IsWindows())
    {
        dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
}

// ---------------------------------------------------------------- Autentificare AD
var authMode = builder.Configuration["Authentication:Mode"] ?? "Negotiate";
if (authMode.Equals(DevelopmentAuthHandler.SchemeName, StringComparison.OrdinalIgnoreCase))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Development authentication is allowed only in the Development environment.");
    }

    builder.Services.AddAuthentication(DevelopmentAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>(DevelopmentAuthHandler.SchemeName, null);
}
else
{
    // Windows Integrated (Kerberos/NTLM) – pe IIS/Windows grupurile AD sunt rezolvate de WindowsPrincipal.
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
}

builder.Services.AddTransient<IClaimsTransformation, AdGroupClaimsTransformation>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AppRoles.Admin, p => p.RequireRole(AppRoles.Admin))
    .AddPolicy(AppRoles.Approver, p => p.RequireRole(AppRoles.Approver))
    .AddPolicy(AppPolicies.Viewer, p => p.RequireRole(AppRoles.Admin, AppRoles.Approver))
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build());

// ---------------------------------------------------------------- Localizare ro/en
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    string[] cultures = ["ro", "en"];
    o.SetDefaultCulture("ro").AddSupportedCultures(cultures).AddSupportedUICultures(cultures);
    o.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
});

var app = builder.Build();

// Prima pornire: creează baza de date în SQL Server și aplică migrările; apoi setările inițiale (Setup:*).
await app.InitializeAsync();

// ---------------------------------------------------------------- Pipeline
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.Use((context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    // SPA-ul și API-ul sunt servite de aceeași aplicație: totul de pe aceeași origine.
    headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    return next(context);
});
app.UseRequestLocalization();

// SPA-ul React (build-ul din frontend copiat în wwwroot la publicare).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
// Auditul înconjoară autorizarea, ca să prindă și cererile refuzate (401/403).
app.UseMiddleware<AuditMiddleware>();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// Readiness pentru monitorizare / după deploy: baza de date accesibilă și fără migrări neaplicate.
app.MapGet("/api/health/ready", async (MDel.Infrastructure.Persistence.MDelDbContext db, CancellationToken ct) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        var pending = canConnect && db.Database.IsRelational()
            ? (await db.Database.GetPendingMigrationsAsync(ct)).ToList()
            : [];
        var ready = canConnect && pending.Count == 0;
        return Results.Json(new { status = ready ? "ready" : "not-ready", database = canConnect, pendingMigrations = pending },
            statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "not-ready", database = false, error = ex.GetType().Name },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();
if (authMode.Equals(DevelopmentAuthHandler.SchemeName, StringComparison.OrdinalIgnoreCase))
{
    // Doar Development: schimbă utilizatorul simulat din browser (ex. ca să aprobi cu alt cont).
    // /api/dev/login-as?user=CORP\ion.popescu&groups=CORP\MDel-Approvers  (groups=none = fără grupuri)
    // /api/dev/login-as  (fără parametri) = revine la utilizatorul din appsettings.Development.json
    app.MapGet("/api/dev/login-as", (string? user, string? groups, HttpContext http) =>
    {
        var cookie = new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/" };
        if (string.IsNullOrWhiteSpace(user))
        {
            http.Response.Cookies.Delete(DevelopmentAuthHandler.UserCookie, cookie);
            http.Response.Cookies.Delete(DevelopmentAuthHandler.GroupsCookie, cookie);
        }
        else
        {
            http.Response.Cookies.Append(DevelopmentAuthHandler.UserCookie, user.Trim(), cookie);
            if (string.IsNullOrWhiteSpace(groups)) http.Response.Cookies.Delete(DevelopmentAuthHandler.GroupsCookie, cookie);
            else http.Response.Cookies.Append(DevelopmentAuthHandler.GroupsCookie, groups.Trim(), cookie);
        }

        return Results.Redirect("/");
    }).AllowAnonymous();
}

app.MapControllers();
// Rutele necunoscute din afara /api servesc SPA-ul (routing client-side).
// Doar căile fără extensie (rute React); un fișier static lipsă rămâne 404, nu index.html.
app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/).*$)}", "index.html").AllowAnonymous();

app.Run();

// Expus pentru WebApplicationFactory în teste.
public partial class Program;
