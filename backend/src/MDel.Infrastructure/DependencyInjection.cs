using MDel.Application.Abstractions;
using MDel.Infrastructure.Auditing;
using MDel.Infrastructure.Delegations;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Persistence;
using MDel.Infrastructure.Settings;
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
        services.AddSingleton(new SettingsValidator(
            configuration.GetValue<long>("Uploads:MaxRequestBodyBytes", 110L * 1024 * 1024)));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<AttachmentStorageProbe>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ForwardingManager>();
        services.AddScoped<DelegationService>();
        services.AddScoped<AttachmentService>();

        if (configuration.GetValue("Integrations:UseFakes", false))
        {
            services.AddSingleton<IDirectoryService, FakeDirectoryService>();
            services.AddSingleton<IExchangeService, FakeExchangeService>();
            services.AddSingleton<IMailService, FakeMailService>();
        }
        else
        {
            services.AddSingleton<IDirectoryService, LdapDirectoryService>();
            services.AddSingleton<IExchangeService, ExchangePowerShellService>();
            services.AddSingleton<IMailService, SmtpMailService>();
        }

        return services;
    }
}
