using MDel.Api.Auth;
using MDel.Api.Middleware;
using MDel.Application.Abstractions;
using MDel.Application.Common;
using MDel.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- Limite upload
// Atașamente de max. 100 MB (configurabil din administrare); limita tehnică a serverului
// trebuie să fie >= setarea din aplicație. Pe IIS se setează și maxAllowedContentLength (web.config).
var maxRequestBytes = builder.Configuration.GetValue<long>("Uploads:MaxRequestBodyBytes", 110L * 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxRequestBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxRequestBytes);

// ---------------------------------------------------------------- Servicii
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers();

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

// ---------------------------------------------------------------- Pipeline
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRequestLocalization();

// SPA-ul React (build-ul din frontend copiat în wwwroot la publicare).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditMiddleware>();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapControllers();
// Rutele necunoscute din afara /api servesc SPA-ul (routing client-side).
app.MapFallbackToFile("{*path:regex(^(?!api/).*$)}", "index.html").AllowAnonymous();

app.Run();

// Expus pentru WebApplicationFactory în teste.
public partial class Program;
