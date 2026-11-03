using CekideApi.Application.Abstractions;
using CekideApi.Application.Notifications;
using CekideApi.Domain;
using CekideApi.Infrastructure.Jobs;
using CekideApi.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CekideApi.UnitTests;

public class OverdueNotificationJobTests
{
    private readonly TestClock _clock = new();
    private readonly InMemoryStudentRepository _students = new();
    private readonly InMemoryPaymentRepository _payments = new();
    private readonly InMemoryOverdueNotificationRepository _notifications = new();
    private readonly FakeEmailSender _email = new();
    private readonly ServiceProvider _provider;

    public OverdueNotificationJobTests()
    {
        _students.Seed("Ana Pérez", "ana@example.com", planId: 1, new DateOnly(2026, 10, 1));

        var services = new ServiceCollection();
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<IStudentRepository>(_students);
        services.AddSingleton<IPaymentRepository>(_payments);
        services.AddSingleton<IOverdueNotificationRepository>(_notifications);
        services.AddSingleton<IEmailSender>(_email);
        services.AddSingleton<IUnitOfWork, FakeUnitOfWork>();
        services.AddScoped<OverdueNotificationService>();
        _provider = services.BuildServiceProvider();
    }

    private OverdueNotificationJob NewJob() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(), _clock, NullLogger<OverdueNotificationJob>.Instance);

    [Theory]
    [InlineData(2026, 11, 1)]
    [InlineData(2026, 11, 10)]
    public async Task Does_nothing_before_day_11_in_buenos_aires(int year, int month, int day)
    {
        _clock.SetBuenosAiresDate(year, month, day);

        var ran = await NewJob().RunOnceAsync();

        Assert.False(ran);
        Assert.Empty(_email.Sent);
        Assert.Empty(_notifications.Items);
    }

    [Fact]
    public async Task Day_10_late_at_night_in_buenos_aires_is_still_before_the_window_even_when_utc_is_day_11()
    {
        // 2026-11-11 01:00 UTC is 2026-11-10 22:00 in Buenos Aires.
        _clock.Set(new DateTimeOffset(2026, 11, 11, 1, 0, 0, TimeSpan.Zero));

        Assert.False(await NewJob().RunOnceAsync());
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task Acts_from_midnight_of_day_11_in_buenos_aires()
    {
        _clock.Set(new DateTimeOffset(2026, 11, 11, 3, 0, 0, TimeSpan.Zero));

        var ran = await NewJob().RunOnceAsync();

        Assert.True(ran);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task Is_idempotent_across_runs_and_restarts()
    {
        _clock.SetBuenosAiresDate(2026, 11, 12);

        await NewJob().RunOnceAsync();
        await NewJob().RunOnceAsync();
        var restarted = NewJob();
        await restarted.RunOnceAsync();
        await restarted.RunOnceAsync();

        Assert.Single(_email.Sent);
        Assert.Single(_notifications.Items);
    }

    [Fact]
    public async Task A_failed_run_is_retried_on_the_next_run()
    {
        _clock.SetBuenosAiresDate(2026, 11, 12);
        _email.FailWith = "Brevo down";
        await NewJob().RunOnceAsync();
        _email.FailWith = null;

        await NewJob().RunOnceAsync();

        Assert.Single(_email.Sent);
        Assert.Equal(NotificationStatus.Sent, Assert.Single(_notifications.Items).Status);
    }

    [Fact]
    public async Task The_background_service_runs_on_start_and_stops_on_cancellation()
    {
        _clock.SetBuenosAiresDate(2026, 11, 12);
        var job = NewJob();

        await job.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _email.Sent.Count == 1);
        await job.StopAsync(CancellationToken.None);

        Assert.Single(_email.Sent);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }
}
