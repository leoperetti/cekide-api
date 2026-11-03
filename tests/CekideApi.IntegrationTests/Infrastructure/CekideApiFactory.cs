using System.Net.Http.Headers;
using CekideApi.Application.Abstractions;
using CekideApi.Application.Notifications;
using CekideApi.Infrastructure.Jobs;
using CekideApi.Infrastructure.Persistence;
using CekideApi.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CekideApi.IntegrationTests.Infrastructure;

/// <summary>Hosts the real API with an in-memory SQLite database, a fake clock and in-memory routine storage.</summary>
public sealed class CekideApiFactory : WebApplicationFactory<Program>
{
    public const string TenantOrigin = "https://cekide-gym.example.com";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CekideApiFactory() => _connection.Open();

    public FakeClock Clock { get; } = new();

    public InMemoryRoutineStorage Routines { get; } = new();

    public FakeEmailSender Emails { get; } = new();

    public FakeAuthcraftInvitationClient Authcraft { get; } = new();

    /// <summary>Runs one overdue-notification pass (what the hourly job does once it is day 11 or later).</summary>
    public async Task<OverdueRunResult> RunOverdueNotificationsAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OverdueNotificationService>().ProcessAsync();
    }

    /// <summary>Runs code against the test database (read or arrange state the API cannot produce).</summary>
    public async Task<T> WithDbAsync<T>(Func<CekideDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<CekideDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestTokens.Key,
                ["Jwt:Issuer"] = TestTokens.Issuer,
                ["Jwt:Audience"] = TestTokens.Audience,
                ["Tenant:Id"] = TestTokens.Tenant,
                ["Cors:AllowedOrigins:0"] = TenantOrigin
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(PolicyProbeController).Assembly);

            services.RemoveAll<DbContextOptions<CekideDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<CekideDbContext>>();
            services.AddDbContext<CekideDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            services.RemoveAll<IRoutineStorage>();
            services.AddSingleton<IRoutineStorage>(Routines);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            services.RemoveAll<IAuthcraftInvitationClient>();
            services.AddSingleton<IAuthcraftInvitationClient>(Authcraft);

            // Tests drive the overdue run explicitly instead of waiting for the hourly job.
            foreach (var job in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(OverdueNotificationJob)).ToList())
            {
                services.Remove(job);
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CekideDbContext>().Database.EnsureCreated();
        return host;
    }

    public HttpClient CreateClientFor(string? role, string subject = "user-1", string? tenant = TestTokens.Tenant)
    {
        var client = CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", TestTokens.Create(role, subject, tenant));
        }

        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
