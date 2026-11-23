using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MDel.Infrastructure.Persistence;

/// <summary>
/// Folosită de `dotnet ef` (migrations add, database update, migrations bundle) fără a porni aplicația web.
/// Connection string-ul „MDel” se citește, în ordine, din:
///   1. variabila de mediu MDEL_CONNECTION_STRING;
///   2. appsettings.{mediu}.json din proiectul MDel.Api (mediu = ASPNETCORE_ENVIRONMENT, implicit Development);
///   3. appsettings.json din proiectul MDel.Api.
/// Pachetul de migrări (efbundle) primește connection string-ul la rulare: efbundle --connection "...".
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MDelDbContext>
{
    public MDelDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";

        var apiDirectory = FindApiDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory ?? Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .Build();

        var connectionString = Environment.GetEnvironmentVariable("MDEL_CONNECTION_STRING")
            ?? configuration.GetConnectionString("MDel")
            ?? "Server=localhost;Database=MDel;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<MDelDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(MDelDbContext).Assembly.FullName);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", MDelDbContext.Schema);
            })
            .Options;

        return new MDelDbContext(options);
    }

    /// <summary>`dotnet ef -s src/MDel.Api` rulează din folderul proiectului de start; căutăm și relativ la el.</summary>
    private static string? FindApiDirectory()
    {
        var cwd = Directory.GetCurrentDirectory();
        foreach (var candidate in new[] { cwd, Path.Combine(cwd, "src", "MDel.Api"), Path.Combine(cwd, "..", "MDel.Api") })
        {
            if (File.Exists(Path.Combine(candidate, "MDel.Api.csproj"))) return Path.GetFullPath(candidate);
        }

        return null;
    }
}
