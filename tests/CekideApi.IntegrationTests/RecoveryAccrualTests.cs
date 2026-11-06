using System.Net;
using System.Net.Http.Json;
using CekideApi.Application.Recoveries;
using CekideApi.Domain;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CekideApi.IntegrationTests;

public sealed class RecoveryAccrualTests : ApiTestBase
{
    [Fact]
    public async Task Current_week_capture_snapshots_eligibility_once_and_excludes_a_midweek_membership()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planId = await CreateLimitedPlanAsync();
        var mondayStudent = await CreateStudentWithPlanAsync("monday@example.com", planId);
        var mondayWeek = await Factory.WithDbAsync(db => db.RecoveryWeeks.AsNoTracking()
            .SingleAsync(week => week.StudentId == mondayStudent));
        var mondayRetried = await CaptureWeekAsync(mondayStudent, new DateOnly(2026, 11, 2));

        Assert.False(mondayRetried);
        Assert.Equal(RecoveryWeekEligibility.Eligible, mondayWeek.Eligibility);
        Assert.Equal(3, mondayWeek.WeeklyTarget);
        Assert.Equal("2026-11", mondayWeek.OriginPeriod);

        Factory.Clock.SetBuenosAiresDate(2026, 11, 5);
        var thursdayPlanId = await CreateLimitedPlanAsync();
        var thursdayStudent = await CreateStudentWithPlanAsync("thursday@example.com", thursdayPlanId);
        var thursdayWeek = await Factory.WithDbAsync(db => db.RecoveryWeeks.AsNoTracking()
            .SingleAsync(week => week.StudentId == thursdayStudent));
        Assert.Equal(RecoveryWeekEligibility.Excluded, thursdayWeek.Eligibility);
    }

    [Fact]
    public async Task Capture_does_not_invent_a_week_snapshot_before_feature_activation()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 5);
        var planId = await CreateLimitedPlanAsync();
        var studentId = await CreateStudentWithPlanAsync("historical@example.com", planId);

        var captured = await CaptureWeekAsync(studentId, new DateOnly(2026, 10, 26));

        Assert.False(captured);
        Assert.Empty(await Factory.WithDbAsync(db => db.RecoveryWeeks.AsNoTracking()
            .Where(week => week.StudentId == studentId && week.WeekStart == new DateOnly(2026, 10, 26))
            .ToListAsync()));
    }

    [Fact]
    public async Task Closing_a_completed_week_creates_only_the_origin_quota_credit_and_is_idempotent()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planId = await CreateLimitedPlanAsync();
        var studentId = await CreateStudentWithPlanAsync("student-1@example.com", planId);
        Assert.Equal(HttpStatusCode.Accepted,
            (await Teacher.PostAsync($"/api/gym/students/{studentId}/invitations", null)).StatusCode);
        var invitation = await Factory.WithDbAsync(db => db.StudentInvitations.AsNoTracking()
            .SingleAsync(item => item.StudentId == studentId));
        Factory.Authcraft.AcceptedProfile = new AcceptedStudentProfile(
            invitation.ExternalInvitationId,
            studentId,
            "student-1",
            Factory.Clock.UtcNow,
            new StudentProfileSnapshot(null, null, "Ana Pérez", "1155550000", null),
            new StudentRegisteredProfile("Ana", "Pérez", "1155550000", "01234567"));

        foreach (var day in new[] { 2, 3 })
        {
            Factory.Clock.SetBuenosAiresDate(2026, 11, day);
            var checkIn = await Student.PostAsJsonAsync(
                "/api/gym/me/attendance/check-in", new { dni = "01234567" });
            Assert.True(checkIn.StatusCode == HttpStatusCode.Created,
                $"{checkIn.StatusCode}: {await checkIn.Content.ReadAsStringAsync()}");
        }

        Factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        Assert.Equal(1, await CloseWeeksAsync(studentId));
        Assert.Equal(0, await CloseWeeksAsync(studentId));
        var week = await Factory.WithDbAsync(db => db.RecoveryWeeks.AsNoTracking()
            .SingleAsync(recoveryWeek => recoveryWeek.StudentId == studentId &&
                recoveryWeek.WeekStart == new DateOnly(2026, 11, 2)));
        var credits = await Factory.WithDbAsync(db => db.RecoveryCredits.AsNoTracking()
            .Where(credit => credit.StudentId == studentId)
            .ToListAsync());
        Assert.Equal(2, week.AttendedCount);
        Assert.Equal(1, week.GrantedCount);
        var credit = Assert.Single(credits);
        Assert.Equal("2026-11", credit.OriginPeriod);
        Assert.Equal(RecoveryCreditStatus.Available, credit.Status);
        Assert.Equal(new DateOnly(2026, 11, 9), credit.NormalStart);
        Assert.Equal(new DateOnly(2026, 12, 1), credit.NormalEndExclusive);
    }

    [Fact]
    public async Task Normalization_releases_cancelled_and_missed_reservations_then_expires_credits_without_extension()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planId = await CreateLimitedPlanAsync();
        var studentId = await CreateStudentWithPlanAsync("normalization@example.com", planId);
        Factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        Assert.Equal(1, await CloseWeeksAsync(studentId));
        var creditIds = await Factory.WithDbAsync(db => db.RecoveryCredits.AsNoTracking()
            .Where(credit => credit.StudentId == studentId)
            .OrderBy(credit => credit.Ordinal)
            .Select(credit => credit.Id)
            .ToListAsync());
        Assert.Equal(3, creditIds.Count);

        await Factory.WithDbAsync(async db =>
        {
            foreach (var (creditId, day) in creditIds.Take(2).Zip(new[] { 10, 11 }))
            {
                var credit = await db.RecoveryCredits.SingleAsync(item => item.Id == creditId);
                var shift = GymShift.Create(new DateOnly(2026, 11, day), new TimeOnly(9, 0), "teacher-1", Factory.Clock.UtcNow);
                db.GymShifts.Add(shift);
                await db.SaveChangesAsync();
                credit.Reserve(shift.LocalDate, Factory.Clock.UtcNow);
                db.RecoveryReservations.Add(RecoveryReservation.Create(studentId, credit, shift, Factory.Clock.UtcNow));
                if (day == 10)
                {
                    shift.Cancel("teacher-1", Factory.Clock.UtcNow);
                }
            }

            await db.SaveChangesAsync();
            return true;
        });

        Factory.Clock.SetBuenosAiresDate(2026, 11, 12);
        Assert.Equal(2, await NormalizeStudentAsync(studentId));
        Assert.Equal(0, await NormalizeStudentAsync(studentId));
        var reservations = await Factory.WithDbAsync(db => db.RecoveryReservations.AsNoTracking()
            .Where(reservation => reservation.StudentId == studentId)
            .OrderBy(reservation => reservation.LocalDate)
            .ToListAsync());
        Assert.Equal(RecoveryReservationStatus.Cancelled, reservations[0].Status);
        Assert.Equal(RecoveryReservationCancelReason.ShiftCancelled, reservations[0].CancelledReason);
        Assert.Equal(RecoveryReservationStatus.Missed, reservations[1].Status);
        Assert.All(await Factory.WithDbAsync(db => db.RecoveryCredits.AsNoTracking()
            .Where(credit => credit.StudentId == studentId)
            .ToListAsync()), credit => Assert.Equal(RecoveryCreditStatus.Available, credit.Status));

        Factory.Clock.SetBuenosAiresDate(2026, 12, 2);
        Assert.Equal(3, await NormalizeStudentAsync(studentId));
        Assert.Equal(0, await NormalizeStudentAsync(studentId));
        Assert.All(await Factory.WithDbAsync(db => db.RecoveryCredits.AsNoTracking()
            .Where(credit => credit.StudentId == studentId)
            .ToListAsync()), credit => Assert.Equal(RecoveryCreditStatus.Expired, credit.Status));
    }

    private async Task<int> CreateLimitedPlanAsync()
    {
        var response = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Recovery {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetInt32();
    }

    private async Task<int> CreateStudentWithPlanAsync(string email, int planId, string? dni = null)
    {
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email,
            dni,
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetInt32();
    }

    private async Task<bool> CaptureWeekAsync(int studentId, DateOnly weekStart)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RecoveryAccrualService>();
        return await service.CaptureWeekAsync(studentId, weekStart);
    }

    private async Task<int> CloseWeeksAsync(int studentId)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RecoveryAccrualService>();
        return await service.CloseDueWeeksForStudentAsync(studentId);
    }

    private async Task<int> NormalizeStudentAsync(int studentId)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RecoveryStateService>();
        return await service.NormalizeStudentAsync(studentId);
    }
}
