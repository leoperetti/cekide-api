using CekideApi.Application.Abstractions;
using CekideApi.Application.Notifications;
using CekideApi.Domain;
using CekideApi.Infrastructure.Jobs;
using CekideApi.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CekideApi.UnitTests;

public class AttendanceQuotaNotificationJobTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    [Fact]
    public async Task Job_runs_due_notifications_and_sent_records_are_terminal()
    {
        var (job, _, notifications, email) = CreateJob();
        notifications.Add(CreateNotice());

        var result = await job.RunOnceAsync();
        var secondResult = await job.RunOnceAsync();

        Assert.Equal(TimeSpan.FromMinutes(1), AttendanceQuotaNotificationJob.Interval);
        Assert.Equal(1, result.Sent);
        Assert.Equal(0, secondResult.Sent);
        Assert.Single(email.Sent);
        Assert.Equal(1, email.Attempts);
        Assert.Equal(AttendanceQuotaNotificationStatus.Sent, notifications.Items[0].Status);
        Assert.Equal(Start.UtcDateTime, notifications.Items[0].SentAt);
    }

    [Fact]
    public async Task Certain_failures_use_one_two_and_five_minute_backoff_then_stop_automatic_retries()
    {
        var (job, clock, notifications, email) = CreateJob();
        notifications.Add(CreateNotice());
        email.FailWith = "provider response contains secret";

        await job.RunOnceAsync();
        Assert.Equal(Start.AddMinutes(1).UtcDateTime, notifications.Items[0].NextAttemptAt);
        Assert.Equal("The email provider rejected the message.", notifications.Items[0].LastError);

        clock.Set(Start.AddMinutes(1));
        await job.RunOnceAsync();
        Assert.Equal(Start.AddMinutes(3).UtcDateTime, notifications.Items[0].NextAttemptAt);

        clock.Set(Start.AddMinutes(3));
        await job.RunOnceAsync();
        Assert.Equal(Start.AddMinutes(8).UtcDateTime, notifications.Items[0].NextAttemptAt);

        clock.Set(Start.AddMinutes(8));
        await job.RunOnceAsync();
        Assert.Equal(AttendanceQuotaNotificationStatus.Failed, notifications.Items[0].Status);
        Assert.Equal(4, notifications.Items[0].Attempts);
        Assert.Null(notifications.Items[0].NextAttemptAt);

        clock.Set(Start.AddHours(1));
        await job.RunOnceAsync();
        Assert.Equal(4, email.Attempts);

        Assert.True(await notifications.QueueManualRetryAsync(notifications.Items[0].Id));
        Assert.Equal(AttendanceQuotaNotificationStatus.Pending, notifications.Items[0].Status);
        Assert.Equal(4, email.Attempts);
    }

    [Fact]
    public async Task Uncertain_send_is_not_retried_automatically()
    {
        var (job, clock, notifications, email) = CreateJob();
        notifications.Add(CreateNotice());
        email.Result = EmailSendResult.Uncertain("provider response lost");

        var firstRun = await job.RunOnceAsync();
        clock.Set(Start.AddHours(1));
        var secondRun = await job.RunOnceAsync();

        Assert.Equal(1, firstRun.DeliveryUnknown);
        Assert.Equal(0, secondRun.Sent);
        Assert.Equal(AttendanceQuotaNotificationStatus.DeliveryUnknown, notifications.Items[0].Status);
        Assert.Equal(1, email.Attempts);
    }

    [Fact]
    public async Task Expired_claim_is_marked_delivery_unknown_and_is_not_sent_again()
    {
        var (job, clock, notifications, email) = CreateJob();
        var notice = CreateNotice();
        notifications.Add(notice);
        Assert.True(await notifications.TryClaimAsync(
            notice.Id, "stale-claim", Start, AttendanceQuotaNotificationPolicy.ClaimLease));

        clock.Set(Start.Add(AttendanceQuotaNotificationPolicy.ClaimLease));
        var result = await job.RunOnceAsync();

        Assert.Equal(1, result.ExpiredClaims);
        Assert.Equal(AttendanceQuotaNotificationStatus.DeliveryUnknown, notice.Status);
        Assert.Equal(0, email.Attempts);
        Assert.False(await notifications.TryClaimAsync(
            notice.Id, "new-claim", clock.UtcNow, AttendanceQuotaNotificationPolicy.ClaimLease));
    }

    [Fact]
    public async Task Two_dispatchers_cannot_claim_the_same_notification()
    {
        var (_, _, notifications, _) = CreateJob();
        var notice = CreateNotice();
        notifications.Add(notice);

        var first = await notifications.TryClaimAsync(
            notice.Id, "first", Start, AttendanceQuotaNotificationPolicy.ClaimLease);
        var second = await notifications.TryClaimAsync(
            notice.Id, "second", Start, AttendanceQuotaNotificationPolicy.ClaimLease);

        Assert.True(first);
        Assert.False(second);
    }

    private static (AttendanceQuotaNotificationJob Job, TestClock Clock,
        InMemoryAttendanceQuotaNotificationRepository Notifications, FakeEmailSender Email) CreateJob()
    {
        var clock = new TestClock();
        clock.Set(Start);
        var notifications = new InMemoryAttendanceQuotaNotificationRepository();
        var email = new FakeEmailSender();
        var service = new AttendanceQuotaNotificationService(
            new InMemoryPlanRepository(),
            new NoopAttendanceRepository(),
            notifications,
            email,
            clock);
        var services = new ServiceCollection();
        services.AddSingleton(service);
        var provider = services.BuildServiceProvider();
        var job = new AttendanceQuotaNotificationJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AttendanceQuotaNotificationJob>.Instance);
        return (job, clock, notifications, email);
    }

    private static AttendanceQuotaNotification CreateNotice() =>
        AttendanceQuotaNotification.Create(
            studentId: 7,
            attendancePeriodId: 4,
            period: "2026-11",
            recipientEmail: "student@example.test",
            recipientName: "Student Name",
            planName: "Three days",
            monthlyLimit: 12,
            utcNow: Start);
}
