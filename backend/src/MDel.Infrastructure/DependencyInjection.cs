using MDel.Application.Abstractions;
using MDel.Infrastructure.Auditing;
using MDel.Infrastructure.Delegations;
using MDel.Infrastructure.Integrations;
using MDel.Infrastructure.Notifications;
using MDel.Infrastructure.Scheduling;
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
        services.AddScoped<AuditQueryService>();
        services.AddScoped<UserProvisioningService>();
        services.AddSingleton(new SettingsValidator(
            configuration.GetValue<long>("Uploads:MaxRequestBodyBytes", 110L * 1024 * 1024)));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<AttachmentStorageProbe>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ForwardingManager>();
        services.AddScoped<DelegationService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<InformGroupService>();
        services.AddScoped<NotificationAdminService>();
        services.AddSingleton<NotificationSignal>();
        services.AddSingleton<NotificationDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<NotificationDispatcher>());
        services.AddScoped<DelegationScheduler>();
        services.AddScoped<RetentionService>();
        services.AddHostedService<SchedulerHostedService>();

        // Integrations:UseFakes = true (Development) înlocuiește AD, Exchange și SMTP cu implementări in-memory.
        // Fiecare se poate alege separat: Integrations:FakeDirectory / FakeExchange / FakeMail.
        // AD-ul simulat se folosește doar cât timp în Setări → Active Directory nu e completat serverul sau domeniul.
        var useFakes = configuration.GetValue("Integrations:UseFakes", false);
        services.AddSingleton<LdapDirectoryService>();
        services.AddSingleton<FakeDirectoryService>();
        switch (configuration.GetValue<bool?>("Integrations:FakeDirectory"))
        {
            case true:
                services.AddSingleton<IDirectoryService>(sp => sp.GetRequiredService<FakeDirectoryService>());
                break;
            case false:
                services.AddSingleton<IDirectoryService>(sp => sp.GetRequiredService<LdapDirectoryService>());
                break;
            default:
                if (useFakes) services.AddSingleton<IDirectoryService, ConfigurableDirectoryService>();
                else services.AddSingleton<IDirectoryService>(sp => sp.GetRequiredService<LdapDirectoryService>());
                break;
        }

        if (configuration.GetValue("Integrations:FakeExchange", useFakes))
            services.AddSingleton<IExchangeService, FakeExchangeService>();
        else
            services.AddSingleton<IExchangeService, ExchangePowerShellService>();

        if (configuration.GetValue("Integrations:FakeMail", useFakes))
            services.AddSingleton<IMailService, FakeMailService>();
        else
            services.AddSingleton<IMailService, SmtpMailService>();

        return services;
    }
}
