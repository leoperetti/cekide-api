using Azure.Core;
using CekideApi.Application.Abstractions;
using CekideApi.Infrastructure.Email;
using CekideApi.Infrastructure.Invitations;
using CekideApi.Infrastructure.Jobs;
using CekideApi.Infrastructure.Persistence;
using CekideApi.Infrastructure.Persistence.Repositories;
using CekideApi.Infrastructure.Storage;
using CekideApi.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CekideApi.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringKey = "CekideDatabaseConnectionString";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<CekideDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var connectionString = configuration[ConnectionStringKey] ?? configuration.GetConnectionString(ConnectionStringKey);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Configuration key '{ConnectionStringKey}' is required (user-secrets or Key Vault).");
            }

            options.UseSqlServer(connectionString);
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IPassTariffRepository, PassTariffRepository>();
        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IOverdueNotificationRepository, OverdueNotificationRepository>();
        services.AddScoped<IStudentInvitationRepository, StudentInvitationRepository>();
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IAttendanceQuotaNotificationRepository, AttendanceQuotaNotificationRepository>();
        services.AddScoped<IKioskSessionRepository, KioskSessionRepository>();
        services.AddScoped<IAttendanceTransaction, AttendanceTransaction>();
        services.AddScoped<IRecoveryTransaction, RecoveryTransaction>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IRecoveryRepository, RecoveryRepository>();

        services.AddSingleton(provider =>
            provider.GetRequiredService<IConfiguration>().GetSection(AuthcraftOptions.SectionName).Get<AuthcraftOptions>() ?? new AuthcraftOptions());
        services.AddHttpClient<IAuthcraftInvitationClient, AuthcraftInvitationClient>();

        services.AddSingleton(provider =>
            provider.GetRequiredService<IConfiguration>().GetSection(BrevoOptions.SectionName).Get<BrevoOptions>() ?? new BrevoOptions());
        services.AddHttpClient<IEmailSender, BrevoEmailSender>();
        services.AddHostedService<AttendanceQuotaNotificationJob>();
        services.AddHostedService<OverdueNotificationJob>();
        services.AddHostedService<RecoveryAccrualJob>();

        services.AddSingleton<IRoutineStorage>(provider =>
        {
            var options = provider.GetRequiredService<IConfiguration>().GetSection(StorageOptions.SectionName).Get<StorageOptions>()
                          ?? new StorageOptions();
            var environment = provider.GetRequiredService<IHostEnvironment>();
            return options.Provider switch
            {
                "AzureBlob" => AzureBlobRoutineStorage.Create(
                    options,
                    environment.EnvironmentName,
                    provider.GetRequiredService<TokenCredential>()),
                "Azurite" => AzureBlobRoutineStorage.CreateAzurite(options, environment.EnvironmentName),
                _ => throw new InvalidOperationException("Configuration key 'Storage:Provider' must be 'AzureBlob' or 'Azurite'.")
            };
        });

        return services;
    }
}
