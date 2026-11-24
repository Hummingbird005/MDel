using MDel.Application.Abstractions;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MDel.Api.Setup;

/// <summary>
/// Pașii rulați o dată, la pornirea aplicației, înainte de a primi cereri:
/// <list type="number">
/// <item>crearea / actualizarea bazei de date (migrările EF) – <c>Setup:MigrateDatabaseOnStartup</c>;</item>
/// <item>setările inițiale din <c>Setup:InitialSettings</c> (ex. AD, grupuri), aplicate doar dacă în bază sunt goale.</item>
/// </list>
/// </summary>
public static class StartupTasks
{
    public static async Task InitializeAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("MDel.Setup");
        var configuration = services.GetRequiredService<IConfiguration>();
        var db = services.GetRequiredService<MDelDbContext>();

        if (!db.Database.IsRelational())
        {
            // Development / teste: baza in-memory (HasData = setările implicite).
            await db.Database.EnsureCreatedAsync();
        }
        else if (configuration.GetValue("Setup:MigrateDatabaseOnStartup", true))
        {
            await MigrateAsync(db, logger);
        }

        await ApplyInitialSettingsAsync(configuration, services.GetRequiredService<ISettingsService>(), logger);

        var initialAdmins = InitialAdmins.Read(configuration);
        if (initialAdmins.Count > 0)
        {
            logger.LogInformation(
                "Setup:InitialAdmins ({Admins}): these accounts are administrators regardless of AD group membership.",
                string.Join(", ", initialAdmins));
        }
    }

    /// <summary>Creează baza dacă nu există și aplică migrările neaplicate (idempotent).</summary>
    private static async Task MigrateAsync(MDelDbContext db, ILogger logger)
    {
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count == 0)
            {
                logger.LogInformation("Database is up to date.");
                return;
            }

            logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied.");
        }
        catch (Exception ex)
        {
            // Fără bază aplicația nu poate funcționa: oprim pornirea, cu mesaj clar în log / Event Viewer.
            logger.LogCritical(ex,
                "Could not create or migrate the database. Check ConnectionStrings:MDel and that the application account " +
                "may create the database (dbcreator) or has db_owner on an existing empty database.");
            throw;
        }
    }

    /// <summary>
    /// Copiază în setări valorile din <c>Setup:InitialSettings</c>:
    /// implicit doar pentru cheile încă necompletate (ce s-a salvat din ecranul Setări nu se suprascrie);
    /// cu <c>Setup:OverwriteSettingsOnStartup = true</c>, la fiecare pornire, peste valorile din bază –
    /// pentru cine configurează aplicația doar din appsettings.json.
    /// Parolele (ex. <c>Ad.ServicePassword</c>) se salvează criptate, ca din ecranul Setări.
    /// </summary>
    private static async Task ApplyInitialSettingsAsync(IConfiguration configuration, ISettingsService settings, ILogger logger)
    {
        var configured = configuration.GetSection("Setup:InitialSettings").GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToDictionary(c => c.Key, c => c.Value!.Trim(), StringComparer.OrdinalIgnoreCase);
        if (configured.Count == 0) return;

        var overwrite = configuration.GetValue("Setup:OverwriteSettingsOnStartup", false);
        var current = (await settings.GetAllAsync()).ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
        var toApply = new Dictionary<string, string?>();
        foreach (var (key, value) in configured)
        {
            if (!current.TryGetValue(key, out var setting))
            {
                logger.LogWarning("Setup:InitialSettings: unknown setting '{Key}' ignored.", key);
                continue;
            }

            if (setting.IsSecret)
            {
                if (!setting.HasValue || (overwrite && !await SecretEqualsAsync(settings, setting.Key, value)))
                    toApply[setting.Key] = value;
            }
            else if (string.IsNullOrWhiteSpace(setting.Value) || (overwrite && setting.Value != value))
            {
                toApply[setting.Key] = value;
            }
        }

        if (toApply.Count == 0) return;

        try
        {
            await settings.UpdateAsync(toApply);
            logger.LogInformation("Initial settings applied: {Keys}", string.Join(", ", toApply.Keys));
        }
        catch (SettingsValidationException ex)
        {
            throw new InvalidOperationException("Setup:InitialSettings contains invalid values: " +
                string.Join("; ", ex.Errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}")), ex);
        }
    }

    /// <summary>Parola salvată e aceeași? (Fără rescriere și audit la fiecare pornire.)</summary>
    private static async Task<bool> SecretEqualsAsync(ISettingsService settings, string key, string value)
    {
        try
        {
            return await settings.GetSecretAsync(key) == value;
        }
        catch
        {
            return false; // ex. cheile Data Protection s-au schimbat: o rescriem
        }
    }
}

/// <summary>
/// Conturile din <c>Setup:InitialAdmins</c> primesc rolul de administrator indiferent de grupurile AD –
/// pentru prima conectare, înainte de configurarea grupurilor în Setări.
/// </summary>
public static class InitialAdmins
{
    public static IReadOnlyList<string> Read(IConfiguration configuration) =>
        configuration.GetSection("Setup:InitialAdmins").Get<string[]>()?
            .Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToArray() ?? [];

    /// <summary>„CONTOSO\user” se compară cu numele complet; „user” se compară doar cu numele contului.</summary>
    public static bool Contains(IConfiguration configuration, string loginName) =>
        Read(configuration).Any(admin => admin.Contains('\\')
            ? admin.Equals(loginName, StringComparison.OrdinalIgnoreCase)
            : admin.Equals(UserProvisioningService.NormalizeSam(loginName), StringComparison.OrdinalIgnoreCase));
}
