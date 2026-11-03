using CekideApi.Application.Abstractions;
using CekideApi.Application.Notifications;
using CekideApi.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CekideApi.Infrastructure.Jobs;

/// <summary>
/// Hourly job that sends the overdue notices once it is day 11 or later in Buenos Aires.
/// It is safe to run repeatedly or from several instances: the service never repeats a notice.
/// </summary>
public sealed class OverdueNotificationJob(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<OverdueNotificationJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>Returns false when it is still inside the payment window (days 1-10) and nothing was done.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (GymCalendar.Today(clock.UtcNow).Day <= PaymentRules.LastDayOfPaymentWindow)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<OverdueNotificationService>().ProcessAsync(cancellationToken);
        logger.LogInformation("Overdue notifications processed: {Sent} sent, {Failed} failed.", result.Sent, result.Failed);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The overdue notification run failed; it will be retried on the next tick.");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
