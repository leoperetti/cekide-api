using CekideApi.Application.Abstractions;
using CekideApi.Application.Recoveries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CekideApi.Infrastructure.Jobs;

public readonly record struct RecoveryAccrualRunResult(int CapturedWeeks, int ClosedWeeks, int NormalizedStates);

public sealed class RecoveryAccrualJob(
    IServiceScopeFactory scopeFactory,
    ILogger<RecoveryAccrualJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public async Task<RecoveryAccrualRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var accrual = services.GetRequiredService<RecoveryAccrualService>();
        var captured = await accrual.CaptureCurrentWeekAsync(cancellationToken);
        var closed = await accrual.CloseDueWeeksAsync(cancellationToken);
        var studentIds = await services.GetRequiredService<IRecoveryRepository>()
            .GetStudentIdsAsync(cancellationToken);
        var normalized = 0;
        var state = services.GetRequiredService<RecoveryStateService>();
        foreach (var studentId in studentIds)
        {
            normalized += await state.NormalizeStudentAsync(studentId, cancellationToken);
        }

        var result = new RecoveryAccrualRunResult(captured, closed, normalized);
        logger.LogInformation(
            "Recovery accrual run completed: {CapturedWeeks} weeks captured, {ClosedWeeks} weeks closed, {NormalizedStates} states normalized.",
            result.CapturedWeeks,
            result.ClosedWeeks,
            result.NormalizedStates);
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
                logger.LogError(exception, "The recovery accrual run failed; it will be retried on the next tick.");
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
