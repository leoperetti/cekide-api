using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class AttendanceQuotaNotificationTests
{
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    [Fact]
    public void Pending_notification_can_be_claimed_and_marked_sent()
    {
        var notice = CreateNotice();

        Assert.True(notice.TryClaim("claim-1", CreatedAt, TimeSpan.FromMinutes(2)));
        Assert.False(notice.TryClaim("claim-2", CreatedAt.AddMinutes(1), TimeSpan.FromMinutes(2)));
        notice.MarkSent("claim-1", CreatedAt.AddSeconds(30));

        Assert.Equal(AttendanceQuotaNotificationStatus.Sent, notice.Status);
        Assert.Equal(1, notice.Attempts);
        Assert.Equal(CreatedAt.AddSeconds(30).UtcDateTime, notice.SentAt);
        Assert.Null(notice.ClaimId);
        Assert.False(notice.TryClaim("claim-2", CreatedAt.AddHours(1), TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Failed_delivery_cannot_retry_before_its_backoff()
    {
        var notice = CreateNotice();
        Assert.True(notice.TryClaim("claim-1", CreatedAt, TimeSpan.FromMinutes(2)));
        notice.MarkFailed("claim-1", "provider rejected", CreatedAt.AddMinutes(1));

        Assert.False(notice.TryClaim("claim-2", CreatedAt.AddSeconds(59), TimeSpan.FromMinutes(2)));
        Assert.True(notice.TryClaim("claim-2", CreatedAt.AddMinutes(1), TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Delivery_unknown_requires_explicit_audited_resolution()
    {
        var notice = CreateNotice();
        Assert.True(notice.TryClaim("claim-1", CreatedAt, TimeSpan.FromMinutes(2)));
        notice.MarkDeliveryUnknown("claim-1", "delivery timed out");

        Assert.False(notice.TryClaim("claim-2", CreatedAt.AddHours(1), TimeSpan.FromMinutes(2)));
        notice.ResolveDelivery("owner-subject", delivered: true, "provider receipt", CreatedAt.AddHours(1));

        Assert.Equal(AttendanceQuotaNotificationStatus.Sent, notice.Status);
        Assert.Equal("owner-subject", notice.ResolvedBy);
        Assert.Equal("provider receipt", notice.ResolutionEvidence);
        Assert.Equal(1, notice.Attempts);
    }

    [Fact]
    public void Expired_sending_claim_becomes_delivery_unknown_and_cannot_be_retried_automatically()
    {
        var notice = CreateNotice();
        Assert.True(notice.TryClaim("claim-1", CreatedAt, TimeSpan.FromMinutes(2)));

        notice.ExpireClaim(CreatedAt.AddMinutes(2));

        Assert.Equal(AttendanceQuotaNotificationStatus.DeliveryUnknown, notice.Status);
        Assert.Null(notice.ClaimId);
        Assert.Null(notice.ClaimedUntil);
        Assert.Equal(1, notice.Attempts);
        Assert.False(notice.TryClaim("claim-2", CreatedAt.AddHours(1), TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Manual_retry_only_queues_a_failed_notification_without_sending_it()
    {
        var notice = CreateNotice();
        Assert.True(notice.TryClaim("claim-1", CreatedAt, TimeSpan.FromMinutes(2)));
        notice.MarkFailed("claim-1", "provider rejected", CreatedAt.AddMinutes(1));

        notice.QueueManualRetry();

        Assert.Equal(AttendanceQuotaNotificationStatus.Pending, notice.Status);
        Assert.Null(notice.NextAttemptAt);
        Assert.Null(notice.ClaimId);
    }

    [Fact]
    public void Manual_retry_rejects_pending_or_delivery_unknown_notifications()
    {
        var notice = CreateNotice();

        Assert.Throws<ConflictException>(() => notice.QueueManualRetry());
        Assert.True(notice.TryClaim("claim-1", CreatedAt, TimeSpan.FromMinutes(2)));
        notice.MarkDeliveryUnknown("claim-1", "delivery timed out");

        Assert.Throws<ConflictException>(() => notice.QueueManualRetry());
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
            utcNow: CreatedAt,
            recommendedPlanId: 2,
            recommendedPlanName: "Four days",
            recommendedMonthlyDays: 16,
            recommendedPrice: 2000m);
}
