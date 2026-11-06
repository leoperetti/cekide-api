using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.Domain;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public sealed class AttendanceNotificationsTests : ApiTestBase
{
    [Fact]
    public async Task Staff_lists_quota_notifications_with_filters_and_private_cache_headers()
    {
        var id = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.DeliveryUnknown);

        foreach (var client in new[] { Owner, Teacher })
        {
            var response = await client.GetAsync("/api/gym/attendance-notifications?status=delivery_unknown");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            var result = await ReadAsync(response);
            var notification = Assert.Single(result.EnumerateArray());
            Assert.Equal(id, notification.GetProperty("id").GetInt32());
            Assert.Equal("delivery_unknown", notification.GetProperty("status").GetString());
            Assert.Equal("2026-11", notification.GetProperty("period").GetString());
            Assert.Equal("Ana Pérez", notification.GetProperty("student_name").GetString());
            Assert.Equal(JsonValueKind.Null, notification.GetProperty("recommended_plan").ValueKind);
            var otherPeriod = await client.GetAsync("/api/gym/attendance-notifications?period=2026-10");
            Assert.Equal(0, (await ReadAsync(otherPeriod)).GetArrayLength());
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Owner.GetAsync("/api/gym/attendance-notifications?status=unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Owner.GetAsync("/api/gym/attendance-notifications?period=11-2026")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Student.GetAsync("/api/gym/attendance-notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous.GetAsync("/api/gym/attendance-notifications")).StatusCode);
    }

    [Fact]
    public async Task Retry_queues_only_failed_notifications_without_sending_synchronously()
    {
        var failedId = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.Failed);
        var pendingId = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.Pending);
        var sendingId = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.Sending);
        var sentId = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.Sent);
        var unknownId = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.DeliveryUnknown);

        var response = await Teacher.PostAsync($"/api/gym/attendance-notifications/{failedId}/retry", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("pending", (await ReadAsync(response)).GetProperty("status").GetString());
        Assert.Equal(0, Factory.Emails.Attempts);
        foreach (var id in new[] { pendingId, sendingId, sentId, unknownId })
        {
            Assert.Equal(HttpStatusCode.Conflict,
                (await Owner.PostAsync($"/api/gym/attendance-notifications/{id}/retry", null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound,
            (await Owner.PostAsync("/api/gym/attendance-notifications/99999/retry", null)).StatusCode);
        var queued = await Factory.RunAttendanceQuotaNotificationsAsync();
        Assert.Equal(2, queued.Sent);
        Assert.Equal(2, Factory.Emails.Sent.Count);
    }

    [Fact]
    public async Task Delivery_resolution_is_audited_and_not_sent_waits_for_explicit_retry()
    {
        var sentId = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.DeliveryUnknown);
        var notSentId = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.DeliveryUnknown);

        var resolved = await Owner.PostAsJsonAsync(
            $"/api/gym/attendance-notifications/{sentId}/resolve-delivery",
            new { outcome = "sent", evidence = "Receipt ABC-123" });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal("sent", (await ReadAsync(resolved)).GetProperty("status").GetString());

        var sentResolution = await Factory.WithDbAsync(async context =>
        {
            var notification = await context.AttendanceQuotaNotifications.SingleAsync(item => item.Id == sentId);
            return (notification.ResolvedBy, notification.ResolutionEvidence, notification.SentAt);
        });
        Assert.Equal("owner-1", sentResolution.ResolvedBy);
        Assert.Equal("Receipt ABC-123", sentResolution.ResolutionEvidence);
        Assert.NotNull(sentResolution.SentAt);

        var notSent = await Teacher.PostAsJsonAsync(
            $"/api/gym/attendance-notifications/{notSentId}/resolve-delivery",
            new { outcome = "not_sent", evidence = "Provider confirmed no delivery" });
        Assert.Equal(HttpStatusCode.OK, notSent.StatusCode);
        Assert.Equal("failed", (await ReadAsync(notSent)).GetProperty("status").GetString());
        Assert.Equal(0, Factory.Emails.Attempts);

        var failedRow = await Factory.WithDbAsync(context =>
            context.AttendanceQuotaNotifications.SingleAsync(item => item.Id == notSentId));
        Assert.Null(failedRow.NextAttemptAt);

        var retry = await Teacher.PostAsync($"/api/gym/attendance-notifications/{notSentId}/retry", null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal("pending", (await ReadAsync(retry)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Invalid_resolution_evidence_and_incompatible_states_are_rejected()
    {
        var pendingId = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.Pending);
        var unknownId = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.DeliveryUnknown);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Teacher.PostAsJsonAsync(
                $"/api/gym/attendance-notifications/{pendingId}/resolve-delivery",
                new { outcome = "sent", evidence = "Receipt" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Teacher.PostAsJsonAsync(
                $"/api/gym/attendance-notifications/{unknownId}/resolve-delivery",
                new { outcome = "sent", evidence = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Teacher.PostAsJsonAsync(
                $"/api/gym/attendance-notifications/{unknownId}/resolve-delivery",
                new { outcome = "unknown", evidence = "Receipt" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Teacher.PostAsJsonAsync(
                $"/api/gym/attendance-notifications/{unknownId}/resolve-delivery",
                new { outcome = "sent", evidence = new string('x', 501) })).StatusCode);
        var resolve = await Owner.PostAsJsonAsync(
            $"/api/gym/attendance-notifications/{unknownId}/resolve-delivery",
            new { outcome = "sent", evidence = "Receipt" });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Teacher.PostAsJsonAsync(
                $"/api/gym/attendance-notifications/{unknownId}/resolve-delivery",
                new { outcome = "sent", evidence = "Receipt" })).StatusCode);
    }

    private async Task<int> CreateNotificationAsync(AttendanceQuotaNotificationStatus status)
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 5);
        var suffix = Guid.NewGuid().ToString("N");
        var plan = await CreatePlanAsync("Quota " + suffix);
        var student = await CreateStudentAsync(
            "Ana Pérez", $"ana-{suffix}@example.test", plan.GetProperty("id").GetInt32());
        var studentId = student.GetProperty("id").GetInt32();
        var planId = plan.GetProperty("id").GetInt32();

        return await Factory.WithDbAsync(async context =>
        {
            var limits = await context.PlanAttendanceLimits.FirstOrDefaultAsync(item => item.PlanId == planId);
            if (limits is null)
            {
                limits = PlanAttendanceLimits.CreateInitial(
                    planId, 3, 12, "test-owner", new DateOnly(2026, 11, 1));
                context.PlanAttendanceLimits.Add(limits);
                await context.SaveChangesAsync();
            }

            var period = AttendancePeriod.Create(
                studentId, planId, limits.Id, "Básico", 3, 12, new DateOnly(2026, 11, 5));
            context.AttendancePeriods.Add(period);
            await context.SaveChangesAsync();

            var notification = AttendanceQuotaNotification.Create(
                studentId,
                period.Id,
                period.Period,
                $"ana-{suffix}@example.test",
                "Ana Pérez",
                period.PlanName,
                period.MonthlyLimit,
                Factory.Clock.UtcNow);
            if (status != AttendanceQuotaNotificationStatus.Pending)
            {
                Assert.True(notification.TryClaim(
                    "seed-claim",
                    Factory.Clock.UtcNow,
                    TimeSpan.FromMinutes(2)));
                switch (status)
                {
                    case AttendanceQuotaNotificationStatus.Sending:
                        break;
                    case AttendanceQuotaNotificationStatus.Sent:
                        notification.MarkSent("seed-claim", Factory.Clock.UtcNow);
                        break;
                    case AttendanceQuotaNotificationStatus.Failed:
                        notification.MarkFailed("seed-claim", "Provider rejected", Factory.Clock.UtcNow.AddMinutes(1));
                        break;
                    case AttendanceQuotaNotificationStatus.DeliveryUnknown:
                        notification.MarkDeliveryUnknown("seed-claim", "Provider response lost");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(status), status, null);
                }
            }

            context.AttendanceQuotaNotifications.Add(notification);
            await context.SaveChangesAsync();
            return notification.Id;
        });
    }
}
