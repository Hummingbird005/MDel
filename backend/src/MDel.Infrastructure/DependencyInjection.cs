using MDel.Application.Abstractions;
using MDel.Infrastructure.Auditing;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MDel")
            ?? throw new InvalidOperationException("Connection string 'MDel' is not configured.");

        services.AddDbContext<MDelDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(MDelDbContext).Assembly.FullName);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", MDelDbContext.Schema);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<UserProvisioningService>();
        services.AddSingleton<SettingsCache>();

        if (configuration.GetValue("Integrations:UseFakes", false))
        {
            services.AddSingleton<IDirectoryService, FakeDirectoryService>();
            services.AddSingleton<IExchangeService, FakeExchangeService>();
            services.AddSingleton<IMailService, FakeMailService>();
        }
        else
        {
            // Implementările reale (LDAP, Exchange PowerShell, SMTP) se adaugă în etapa 1.
            throw new NotSupportedException(
                "Real AD/Exchange/SMTP integrations arrive in stage 1. Set Integrations:UseFakes=true for now.");
        }

        return services;
    }
}
