using System.Net;
using System.Net.Http.Json;
using CekideApi.Domain;
using CekideApi.Infrastructure.Jobs;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CekideApi.IntegrationTests;

public sealed class RecoveryAccrualJobTests : ApiTestBase
{
    [Fact]
    public async Task Run_once_captures_current_week_closes_due_weeks_and_retries_idempotently()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planResponse = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Job {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        Assert.Equal(HttpStatusCode.Created, planResponse.StatusCode);
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();
        var studentResponse = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "job@example.com",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, studentResponse.StatusCode);
        var studentId = (await ReadAsync(studentResponse)).GetProperty("id").GetInt32();

        Factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var job = new RecoveryAccrualJob(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RecoveryAccrualJob>.Instance);

        var firstRun = await job.RunOnceAsync();
        var secondRun = await job.RunOnceAsync();

        Assert.Equal(new RecoveryAccrualRunResult(1, 1, 0), firstRun);
        Assert.Equal(new RecoveryAccrualRunResult(0, 0, 0), secondRun);
        Assert.Equal(2, await Factory.WithDbAsync(db => db.RecoveryWeeks.AsNoTracking()
            .CountAsync(week => week.StudentId == studentId)));
    }
}
