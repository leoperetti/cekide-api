using CekideApi.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CekideApi.Infrastructure.Jobs;

public sealed class AttendanceQuotaNotificationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<AttendanceQuotaNotificationJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public async Task<AttendanceQuotaDispatchResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<AttendanceQuotaNotificationService>()
            .ProcessDueAsync(cancellationToken);
        logger.LogInformation(
            "Attendance quota notifications processed: {Sent} sent, {Failed} failed, {DeliveryUnknown} uncertain, {ExpiredClaims} expired claims.",
            result.Sent,
            result.Failed,
            result.DeliveryUnknown,
            result.ExpiredClaims);
        return result;
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
                logger.LogError(exception, "Attendance quota notification run failed; it will be retried on the next tick.");
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
