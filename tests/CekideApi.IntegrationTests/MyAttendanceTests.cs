using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.Domain;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public sealed class MyAttendanceTests : ApiTestBase
{
    [Fact]
    public async Task Monthly_calendar_orders_dates_and_keeps_current_summary_separate_from_history()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var (planId, studentId) = await CreateStudentWithPlanAsync();

        Factory.Clock.Set(new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero));
        Assert.Equal(HttpStatusCode.Created,
            (await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "01234567" })).StatusCode);
        Factory.Clock.Set(new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero));
        Assert.Equal(HttpStatusCode.Created,
            (await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "01234567" })).StatusCode);
        Factory.Clock.Set(new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero));

        var current = await Student.GetAsync("/api/gym/me/attendance?period=2026-11");

        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal("no-store", current.Headers.CacheControl?.ToString());
        var currentBody = await ReadAsync(current);
        Assert.Equal("2026-11", currentBody.GetProperty("period").GetString());
        Assert.Equal(
            new[] { "2026-11-02", "2026-11-04" },
            currentBody.GetProperty("attendances").EnumerateArray()
                .Select(item => item.GetProperty("local_date").GetString())
                .ToArray());
        Assert.Equal(2, currentBody.GetProperty("period_summary").GetProperty("attended").GetInt32());
        Assert.Equal("2026-11", currentBody.GetProperty("current_summary").GetProperty("period").GetString());
        Assert.Equal(2,
            currentBody.GetProperty("current_summary").GetProperty("month").GetProperty("attended").GetInt32());

        var previous = await Student.GetAsync("/api/gym/me/attendance?period=2026-10");

        Assert.Equal(HttpStatusCode.OK, previous.StatusCode);
        var previousBody = await ReadAsync(previous);
        Assert.Empty(previousBody.GetProperty("attendances").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, previousBody.GetProperty("period_summary").GetProperty("limit").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            previousBody.GetProperty("period_summary").GetProperty("weekly_limit").ValueKind);
        Assert.Equal("2026-11", previousBody.GetProperty("current_summary").GetProperty("period").GetString());
        Assert.Equal(2,
            previousBody.GetProperty("current_summary").GetProperty("month").GetProperty("attended").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Student.GetAsync("/api/gym/me/attendance?period=2026-13")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Student.GetAsync("/api/gym/me/attendance?period=2026-12")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Teacher.GetAsync("/api/gym/me/attendance")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous.GetAsync("/api/gym/me/attendance")).StatusCode);
        Assert.True(planId > 0);
        Assert.True(studentId > 0);
    }

    [Fact]
    public async Task Mixed_calendar_keeps_recovery_charges_in_the_origin_month_when_destination_is_empty()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var (_, studentId) = await CreateStudentWithPlanAsync();
        await SeedMixedDecemberAttendanceAsync(studentId);
        Factory.Clock.Set(new DateTimeOffset(2026, 12, 18, 15, 0, 0, TimeSpan.Zero));

        var destination = await Student.GetAsync("/api/gym/me/attendance?period=2026-12");

        Assert.Equal(HttpStatusCode.OK, destination.StatusCode);
        var destinationBody = await ReadAsync(destination);
        var periodSummary = destinationBody.GetProperty("period_summary");
        Assert.Equal(13, periodSummary.GetProperty("attended").GetInt32());
        Assert.Equal(11, periodSummary.GetProperty("ordinary_attended").GetInt32());
        Assert.Equal(2, periodSummary.GetProperty("recovery_attended").GetInt32());
        Assert.Equal(11, periodSummary.GetProperty("consumed").GetInt32());
        Assert.Equal(1, periodSummary.GetProperty("remaining").GetInt32());
        Assert.Equal(0, periodSummary.GetProperty("exceeded").GetInt32());
        Assert.Equal(0, periodSummary.GetProperty("recovery_held").GetInt32());
        Assert.Equal(13, destinationBody.GetProperty("attendances").GetArrayLength());
        Assert.All(
            destinationBody.GetProperty("attendances").EnumerateArray()
                .Where(item => item.GetProperty("charge_kind").GetString() == "recovery"),
            item => Assert.Equal("2026-11", item.GetProperty("recovery_origin_period").GetString()));

        var origin = await Student.GetAsync("/api/gym/me/attendance?period=2026-11");

        Assert.Equal(HttpStatusCode.OK, origin.StatusCode);
        var originBody = await ReadAsync(origin);
        Assert.Empty(originBody.GetProperty("attendances").EnumerateArray());
        Assert.Equal(0, originBody.GetProperty("period_summary").GetProperty("attended").GetInt32());
        Assert.Equal(2, originBody.GetProperty("period_summary").GetProperty("consumed").GetInt32());
        Assert.Equal(10, originBody.GetProperty("period_summary").GetProperty("remaining").GetInt32());
        Assert.Equal("2026-12", originBody.GetProperty("current_summary").GetProperty("period").GetString());
        Assert.Equal(13,
            originBody.GetProperty("current_summary").GetProperty("month").GetProperty("attended").GetInt32());
    }

    [Fact]
    public async Task First_direct_calendar_access_after_invited_signup_links_the_same_student_without_payments()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var (_, studentId) = await CreateStudentWithPlanAsync();
        Assert.Equal(HttpStatusCode.Accepted,
            (await Teacher.PostAsync($"/api/gym/students/{studentId}/invitations", null)).StatusCode);
        var invitationId = await Factory.WithDbAsync(db => db.StudentInvitations.AsNoTracking()
            .Where(item => item.StudentId == studentId)
            .Select(item => item.ExternalInvitationId)
            .SingleAsync());
        Factory.Authcraft.AcceptedProfile = new AcceptedStudentProfile(
            invitationId,
            studentId,
            "student-1",
            DateTimeOffset.UtcNow,
            new StudentProfileSnapshot("Ana", "Pérez", "Ana Pérez", "1155550000", "01234567"),
            new StudentRegisteredProfile("Ana", "Pérez", "1155550000", "01234567"));

        var response = await Student.GetAsync("/api/gym/me/attendance?period=2026-11");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await ReadAsync(response);
        Assert.Equal(JsonValueKind.Array, body.GetProperty("attendances").ValueKind);
        var linkedStudents = await Factory.WithDbAsync(db => db.Students.AsNoTracking()
            .Where(item => item.IdentityId == "student-1")
            .ToListAsync());
        var linked = Assert.Single(linkedStudents);
        Assert.Equal(studentId, linked.Id);
        Assert.Equal("01234567", linked.Dni);
        Assert.Single(Factory.Authcraft.Calls, call => call.Operation == "accepted-profile");
    }

    [Fact]
    public async Task Direct_calendar_access_surfaces_upstream_failure_and_retry_links_the_original_student()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var (_, studentId) = await CreateStudentWithPlanAsync();
        Assert.Equal(HttpStatusCode.Accepted,
            (await Teacher.PostAsync($"/api/gym/students/{studentId}/invitations", null)).StatusCode);
        var invitationId = await Factory.WithDbAsync(db => db.StudentInvitations.AsNoTracking()
            .Where(item => item.StudentId == studentId)
            .Select(item => item.ExternalInvitationId)
            .SingleAsync());
        Factory.Authcraft.AcceptedProfile = new AcceptedStudentProfile(
            invitationId,
            studentId,
            "student-1",
            DateTimeOffset.UtcNow,
            new StudentProfileSnapshot("Ana", "Pérez", "Ana Pérez", "1155550000", "01234567"),
            new StudentRegisteredProfile("Ana", "Pérez", "1155550000", "01234567"));
        Factory.Authcraft.FailNext = new UpstreamServiceException("authcraft-api is unavailable.");

        var failure = await Student.GetAsync("/api/gym/me/attendance?period=2026-11");

        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.Equal("no-store", failure.Headers.CacheControl?.ToString());
        Assert.Equal("Bad Gateway", (await ReadAsync(failure)).GetProperty("title").GetString());
        var beforeRetry = await Factory.WithDbAsync(db => db.Students.AsNoTracking()
            .SingleAsync(item => item.Id == studentId));
        Assert.Null(beforeRetry.IdentityId);
        Assert.Equal("01234567", beforeRetry.Dni);

        var retry = await Student.GetAsync("/api/gym/me/attendance?period=2026-11");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var linkedStudents = await Factory.WithDbAsync(db => db.Students.AsNoTracking()
            .Where(item => item.IdentityId == "student-1")
            .ToListAsync());
        var linked = Assert.Single(linkedStudents);
        Assert.Equal(studentId, linked.Id);
        Assert.Equal("01234567", linked.Dni);
        Assert.Equal(2, Factory.Authcraft.Calls.Count(call => call.Operation == "accepted-profile"));
    }

    private async Task<(int PlanId, int StudentId)> CreateStudentWithPlanAsync()
    {
        var planResponse = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Attendance {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        Assert.Equal(HttpStatusCode.Created, planResponse.StatusCode);
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();
        var studentResponse = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "student-1@example.com",
            phone = "1155550000",
            dni = "01234567",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, studentResponse.StatusCode);
        return (planId, (await ReadAsync(studentResponse)).GetProperty("id").GetInt32());
    }

    private async Task SeedMixedDecemberAttendanceAsync(int studentId)
    {
        await Factory.WithDbAsync(async db =>
        {
            var plan = await db.Plans.SingleAsync();
            var limits = await db.PlanAttendanceLimits.SingleAsync(item => item.PlanId == plan.Id);
            var originPeriod = await db.AttendancePeriods.SingleAsync(
                item => item.StudentId == studentId && item.Period == "2026-11");
            var destinationPeriod = AttendancePeriod.Create(
                studentId, plan.Id, limits.Id, plan.Name, 3, 12, new DateOnly(2026, 12, 1));
            db.AttendancePeriods.Add(destinationPeriod);
            await db.SaveChangesAsync();

            var firstWeek = RecoveryWeek.Create(
                studentId, new DateOnly(2026, 11, 23), originPeriod.Id, limits.Id, 3,
                RecoveryWeekEligibility.Eligible);
            firstWeek.Close(new DateTimeOffset(2026, 12, 1, 15, 0, 0, TimeSpan.Zero), 0, 1);
            var secondWeek = RecoveryWeek.Create(
                studentId, new DateOnly(2026, 11, 30), originPeriod.Id, limits.Id, 3,
                RecoveryWeekEligibility.Eligible);
            secondWeek.Close(new DateTimeOffset(2026, 12, 8, 15, 0, 0, TimeSpan.Zero), 0, 1);
            db.RecoveryWeeks.AddRange(firstWeek, secondWeek);
            await db.SaveChangesAsync();

            var firstCredit = RecoveryCredit.Create(firstWeek, 1);
            firstCredit.Reserve(
                new DateOnly(2026, 12, 3),
                new DateTimeOffset(2026, 11, 30, 15, 0, 0, TimeSpan.Zero));
            firstCredit.Use(new DateTimeOffset(2026, 12, 3, 21, 30, 0, TimeSpan.Zero));
            var secondCredit = RecoveryCredit.Create(secondWeek, 1);
            secondCredit.Reserve(
                new DateOnly(2026, 12, 8),
                new DateTimeOffset(2026, 12, 7, 15, 0, 0, TimeSpan.Zero));
            secondCredit.Use(new DateTimeOffset(2026, 12, 8, 21, 30, 0, TimeSpan.Zero));
            db.RecoveryCredits.AddRange(firstCredit, secondCredit);
            await db.SaveChangesAsync();

            var quota = new AttendanceQuotaSnapshot(0, 0, false, false, false);
            foreach (var day in new[] { 1, 2, 4, 9, 10, 11, 14, 15, 16, 17, 18 })
            {
                var date = new DateOnly(2026, 12, day);
                db.Attendances.Add(Attendance.Create(
                    studentId,
                    destinationPeriod.Id,
                    date,
                    new DateTimeOffset(date.Year, date.Month, date.Day, 15, 0, 0, TimeSpan.Zero),
                    AttendanceSource.Self,
                    null,
                    quota));
            }

            AddRecoveryAttendance(db, studentId, destinationPeriod.Id, firstCredit.Id, new DateOnly(2026, 12, 3));
            AddRecoveryAttendance(db, studentId, destinationPeriod.Id, secondCredit.Id, new DateOnly(2026, 12, 8));
            await db.SaveChangesAsync();
            return true;
        });
    }

    private static void AddRecoveryAttendance(
        CekideApi.Infrastructure.Persistence.CekideDbContext db,
        int studentId,
        int periodId,
        int creditId,
        DateOnly date) =>
        db.Attendances.Add(Attendance.Create(
            studentId,
            periodId,
            date,
            new DateTimeOffset(date.Year, date.Month, date.Day, 21, 30, 0, TimeSpan.Zero),
            AttendanceSource.Self,
            null,
            new AttendanceQuotaSnapshot(0, 0, false, false, false),
            chargeKind: AttendanceChargeKind.Recovery,
            recoveryCreditId: creditId));
}
