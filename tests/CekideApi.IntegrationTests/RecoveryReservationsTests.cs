using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.Domain;
using CekideApi.IntegrationTests.Infrastructure;
using CekideApi.Application.Recoveries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CekideApi.IntegrationTests;

public sealed class RecoveryReservationsTests : ApiTestBase
{
    private const string StudentEmail = "student-1@example.com";

    [Fact]
    public async Task First_direct_recovery_access_links_the_invited_student_and_returns_private_credit_data()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planResponse = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Recovery {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        Assert.Equal(HttpStatusCode.Created, planResponse.StatusCode);
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();
        var student = await CreateStudentAsync(email: StudentEmail, planId: planId);
        var studentId = student.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Accepted,
            (await Teacher.PostAsync($"/api/gym/students/{studentId}/invitations", null)).StatusCode);
        Factory.Authcraft.AcceptedProfile = new AcceptedStudentProfile(
            100,
            studentId,
            "student-1",
            DateTimeOffset.UtcNow,
            new StudentProfileSnapshot(null, null, "Ana Pérez", "1155550000", null),
            new StudentRegisteredProfile("Ana", "Pérez", "1155550000", "01234567"));

        var response = await Student.GetAsync("/api/gym/me/recovery-credits");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await ReadAsync(response);
        Assert.Equal(GymCalendar.TimeZoneId, body.GetProperty("time_zone").GetString());
        Assert.True(body.TryGetProperty("as_of_date", out _));
        Assert.True(body.GetProperty("credits").ValueKind == JsonValueKind.Array);
        var linked = await Factory.WithDbAsync(db => db.Students.AsNoTracking().SingleAsync(s => s.Id == studentId));
        Assert.Equal("student-1", linked.IdentityId);
        Assert.Equal("01234567", linked.Dni);
        Assert.Contains(Factory.Authcraft.Calls, call => call.Operation == "accepted-profile");
    }

    [Fact]
    public async Task First_direct_recovery_access_links_a_student_with_a_preloaded_dni_without_creating_a_second_record()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planResponse = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Recovery {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();
        var studentId = (await CreateStudentAsync(email: StudentEmail, planId: planId))
            .GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Accepted,
            (await Teacher.PostAsync($"/api/gym/students/{studentId}/invitations", null)).StatusCode);
        Factory.Authcraft.AcceptedProfile = new AcceptedStudentProfile(
            100,
            studentId,
            "student-1",
            DateTimeOffset.UtcNow,
            new StudentProfileSnapshot("Ana", "Pérez", "Ana Pérez", "1155550000", "01234567"),
            new StudentRegisteredProfile("Ana", "Pérez", "1155550000", "01234567"));

        var response = await Student.GetAsync("/api/gym/me/recovery-credits");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var students = await Factory.WithDbAsync(db => db.Students.AsNoTracking()
            .Where(item => item.IdentityId == "student-1")
            .ToListAsync());
        var linked = Assert.Single(students);
        Assert.Equal(studentId, linked.Id);
        Assert.Equal("01234567", linked.Dni);
        Assert.Single(Factory.Authcraft.Calls, call => call.Operation == "accepted-profile");
    }

    [Fact]
    public async Task Availability_and_reservation_reject_periods_and_dates_outside_credit_windows()
    {
        var creditId = await CreateRecoveryCreditAsync();
        using var otherStudent = Factory.CreateClientFor("user", "other-student");
        var foreignCreditId = await CreateRecoveryCreditAsync(
            "other-student@example.com", otherStudent, "87654321");
        var foreignCredit = await Student.GetAsync(
            $"/api/gym/me/recovery-availability?credit_id={foreignCreditId}&period=2026-11");

        Assert.Equal(HttpStatusCode.NotFound, foreignCredit.StatusCode);
        Assert.Equal("no-store", foreignCredit.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Student.GetAsync(
                $"/api/gym/me/recovery-availability?credit_id={creditId}&period=2027-01")).StatusCode);

        var outOfWindowShiftId = await CreateShiftAsync("2026-12-01");
        var outOfWindowReservation = await Student.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = outOfWindowShiftId });
        Assert.Equal(HttpStatusCode.Conflict, outOfWindowReservation.StatusCode);
        Assert.Equal(
            "recovery_window_closed",
            (await ReadAsync(outOfWindowReservation)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reservation_conflicts_are_explicit_and_cancellation_retries_are_idempotent()
    {
        var creditId = await CreateRecoveryCreditAsync();
        Factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var firstShift = await CreateShiftAsync("2026-11-10");
        var secondShift = await CreateShiftAsync("2026-11-11");
        var created = await Student.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = firstShift });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var conflict = await Student.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = secondShift });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("no-store", conflict.Headers.CacheControl?.ToString());

        var reservationId = (await ReadAsync(created)).GetProperty("id").GetInt32();
        var cancelled = await Student.PostAsync(
            $"/api/gym/me/recovery-reservations/{reservationId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("no-store", cancelled.Headers.CacheControl?.ToString());
        var retry = await Student.PostAsync(
            $"/api/gym/me/recovery-reservations/{reservationId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("cancelled", (await ReadAsync(retry)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Availability_and_reservation_responses_include_only_authorized_dates_and_staff_capacity()
    {
        var creditId = await CreateRecoveryCreditAsync();
        Factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var shiftId = await CreateShiftAsync("2026-11-10");

        var availability = await Student.GetAsync(
            $"/api/gym/me/recovery-availability?credit_id={creditId}&period=2026-11");
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        Assert.Equal("no-store", availability.Headers.CacheControl?.ToString());
        var available = await ReadAsync(availability);
        Assert.Contains("2026-11-10", available.GetProperty("allowed_dates").EnumerateArray()
            .Select(item => item.GetString()));
        var shift = Assert.Single(available.GetProperty("shifts").EnumerateArray());
        Assert.Equal(shiftId, shift.GetProperty("id").GetInt32());
        Assert.Equal(12, shift.GetProperty("capacity").GetInt32());
        Assert.Equal(0, shift.GetProperty("planned_count").GetInt32());
        Assert.True(shift.GetProperty("reservable").GetBoolean());
        Assert.False(shift.TryGetProperty("student_id", out _));
        Assert.False(shift.TryGetProperty("email", out _));

        var created = await Student.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = shiftId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var personal = await Student.GetAsync("/api/gym/me/recovery-reservations?period=2026-11");
        Assert.Equal(HttpStatusCode.OK, personal.StatusCode);
        Assert.Equal(1, (await ReadAsync(personal)).GetArrayLength());

        var occupancy = await Teacher.GetAsync($"/api/gym/shifts/{shiftId}/occupancy");
        Assert.Equal(HttpStatusCode.OK, occupancy.StatusCode);
        var staff = await ReadAsync(occupancy);
        Assert.Equal(1, staff.GetProperty("planned_count").GetInt32());
        Assert.Equal(1, staff.GetProperty("reserved_count").GetInt32());
        Assert.Equal("Ana Pérez", staff.GetProperty("reservations")[0].GetProperty("student_name").GetString());
    }

    [Fact]
    public async Task Matching_check_in_uses_the_confirmed_recovery_credit_and_updates_origin_accounting()
    {
        var creditId = await CreateRecoveryCreditAsync();
        Factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var shiftId = await CreateShiftAsync("2026-11-10");
        var reserved = await Student.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = shiftId });
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);

        Factory.Clock.Set(new DateTimeOffset(2026, 11, 10, 21, 30, 0, TimeSpan.Zero));
        var checkIn = await Student.PostAsJsonAsync(
            "/api/gym/me/attendance/check-in", new { dni = "01234567" });

        Assert.Equal(HttpStatusCode.Created, checkIn.StatusCode);
        var body = await ReadAsync(checkIn);
        var attendance = body.GetProperty("attendance");
        Assert.Equal("recovery", attendance.GetProperty("charge_kind").GetString());
        Assert.Equal(creditId, attendance.GetProperty("recovery_credit_id").GetInt32());
        Assert.Equal("2026-11", attendance.GetProperty("recovery_origin_period").GetString());
        Assert.Equal(3, body.GetProperty("summary").GetProperty("month").GetProperty("consumed").GetInt32());
        Assert.Equal(3, body.GetProperty("summary").GetProperty("month").GetProperty("attended").GetInt32());

        var studentId = await Factory.WithDbAsync(db => db.Students.AsNoTracking()
            .Where(student => student.IdentityId == "student-1")
            .Select(student => student.Id)
            .SingleAsync());
        var state = await Factory.WithDbAsync(async db =>
        {
            var reservation = await db.RecoveryReservations.AsNoTracking()
                .SingleAsync(item => item.StudentId == studentId);
            var credit = await db.RecoveryCredits.AsNoTracking().SingleAsync(item => item.Id == creditId);
            return (ReservationStatus: reservation.Status, CreditStatus: credit.Status);
        });
        Assert.Equal(RecoveryReservationStatus.Attended, state.ReservationStatus);
        Assert.Equal(RecoveryCreditStatus.Used, state.CreditStatus);
    }

    [Fact]
    public async Task Recovery_from_a_previous_origin_month_counts_as_attendance_but_not_destination_month_consumption()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 23);
        var planResponse = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Cross month {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        Assert.Equal(HttpStatusCode.Created, planResponse.StatusCode);
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();
        var studentResponse = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = StudentEmail,
            phone = "1155550000",
            dni = "01234567",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, studentResponse.StatusCode);
        var studentId = (await ReadAsync(studentResponse)).GetProperty("id").GetInt32();
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

        Assert.Equal(HttpStatusCode.OK, (await Student.GetAsync("/api/gym/me/recovery-credits")).StatusCode);
        Factory.Clock.SetBuenosAiresDate(2026, 11, 30);
        using (var scope = Factory.Services.CreateScope())
        {
            var accrual = scope.ServiceProvider.GetRequiredService<RecoveryAccrualService>();
            Assert.Equal(1, await accrual.CloseDueWeeksForStudentAsync(studentId));
        }

        var creditsResponse = await Student.GetAsync("/api/gym/me/recovery-credits");
        Assert.Equal(HttpStatusCode.OK, creditsResponse.StatusCode);
        var creditId = (await ReadAsync(creditsResponse)).GetProperty("credits").EnumerateArray()
            .First(credit => credit.GetProperty("origin_period").GetString() == "2026-11")
            .GetProperty("id").GetInt32();
        var shiftId = await CreateShiftAsync("2026-12-01");
        var reserved = await Student.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = shiftId });
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);

        Factory.Clock.Set(new DateTimeOffset(2026, 12, 1, 21, 30, 0, TimeSpan.Zero));
        var checkIn = await Student.PostAsJsonAsync(
            "/api/gym/me/attendance/check-in", new { dni = "01234567" });
        Assert.Equal(HttpStatusCode.Created, checkIn.StatusCode);
        var body = await ReadAsync(checkIn);
        Assert.Equal(1, body.GetProperty("summary").GetProperty("month").GetProperty("attended").GetInt32());
        Assert.Equal(1, body.GetProperty("summary").GetProperty("month").GetProperty("recovery_attended").GetInt32());
        Assert.Equal(0, body.GetProperty("summary").GetProperty("month").GetProperty("consumed").GetInt32());
        Assert.Equal(0, body.GetProperty("summary").GetProperty("month").GetProperty("recovery_held").GetInt32());
        Assert.Equal(AttendanceChargeKind.Recovery.ToString().ToLowerInvariant(),
            body.GetProperty("attendance").GetProperty("charge_kind").GetString());
        Assert.Empty(await Factory.WithDbAsync(db => db.AttendanceQuotaNotifications.AsNoTracking()
            .Where(item => item.StudentId == studentId)
            .ToListAsync()));
    }

    [Fact]
    public async Task Reservation_body_cannot_choose_student_date_or_quota_and_retry_is_not_success_shaped()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/gym/me/recovery-reservations")
        {
            Content = JsonContent.Create(new
            {
                credit_id = 1,
                shift_id = 1,
                student_id = 999,
                local_date = "2026-12-01",
                monthly_limit = 999
            })
        };

        var response = await Student.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var error = await ReadAsync(response);
        Assert.NotEqual(JsonValueKind.Array, error.ValueKind);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Personal_reservations_and_staff_student_history_are_private_and_scoped()
    {
        var student = await CreateStudentAsync(email: StudentEmail);
        var studentId = student.GetProperty("id").GetInt32();

        var mine = await Student.GetAsync("/api/gym/me/recovery-reservations?period=2026-11");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal("no-store", mine.Headers.CacheControl?.ToString());
        Assert.Equal(JsonValueKind.Array, (await ReadAsync(mine)).ValueKind);

        var history = await Teacher.GetAsync(
            $"/api/gym/students/{studentId}/recovery-reservations?period=2026-11");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Equal("no-store", history.Headers.CacheControl?.ToString());
        Assert.Equal(JsonValueKind.Array, (await ReadAsync(history)).ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Student.GetAsync(
                $"/api/gym/students/{studentId}/recovery-reservations?period=2026-11")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Anonymous.GetAsync("/api/gym/me/recovery-reservations?period=2026-11")).StatusCode);
    }

    [Fact]
    public async Task A_reservation_retry_returns_the_same_confirmed_reservation_and_never_accepts_client_dates()
    {
        var creditId = await CreateRecoveryCreditAsync();
        Factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var shiftResponse = await Teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = "2026-11-10",
            starts_at = "18:00"
        });
        Assert.Equal(HttpStatusCode.Created, shiftResponse.StatusCode);
        var shiftId = (await ReadAsync(shiftResponse)).GetProperty("id").GetInt32();
        var response = await Student.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = shiftId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsync(response);

        response = await Student.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = shiftId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var repeated = await ReadAsync(response);

        Assert.Equal(created.GetProperty("id").GetInt32(), repeated.GetProperty("id").GetInt32());
        Assert.Equal("confirmed", repeated.GetProperty("status").GetString());
        Assert.False(repeated.TryGetProperty("student_id", out _));
        Assert.False(repeated.TryGetProperty("monthly_limit", out _));
    }

    [Fact]
    public async Task An_upstream_timeout_does_not_partially_link_the_student_and_a_retry_uses_the_accepted_profile()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planResponse = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Recovery {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();
        var studentId = (await CreateStudentAsync(email: StudentEmail, planId: planId))
            .GetProperty("id").GetInt32();
        await Teacher.PostAsync($"/api/gym/students/{studentId}/invitations", null);
        Factory.Authcraft.FailNext = new UpstreamServiceException("authcraft-api timed out.");

        var first = await Student.GetAsync("/api/gym/me/recovery-credits");
        Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);
        Assert.Equal("no-store", first.Headers.CacheControl?.ToString());
        var unlinked = await Factory.WithDbAsync(db => db.Students.AsNoTracking().SingleAsync(s => s.Id == studentId));
        Assert.Null(unlinked.IdentityId);
        Assert.Null(unlinked.Dni);

        Factory.Authcraft.AcceptedProfile = new AcceptedStudentProfile(
            100,
            studentId,
            "student-1",
            DateTimeOffset.UtcNow,
            new StudentProfileSnapshot(null, null, "Ana Pérez", "1155550000", null),
            new StudentRegisteredProfile("Ana", "Pérez", "1155550000", "01234567"));
        var retry = await Student.GetAsync("/api/gym/me/recovery-credits");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("no-store", retry.Headers.CacheControl?.ToString());
        var linked = await Factory.WithDbAsync(db => db.Students.AsNoTracking().SingleAsync(s => s.Id == studentId));
        Assert.Equal("student-1", linked.IdentityId);
        Assert.Equal("01234567", linked.Dni);
        Assert.Equal(2, Factory.Authcraft.Calls.Count(call => call.Operation == "accepted-profile"));
    }

    [Fact]
    public async Task An_upstream_conflict_is_returned_without_linking_or_falling_back()
    {
        var studentId = (await CreateStudentAsync(email: StudentEmail))
            .GetProperty("id").GetInt32();
        await Teacher.PostAsync($"/api/gym/students/{studentId}/invitations", null);
        Factory.Authcraft.FailNext = new ConflictException("The accepted invitation does not match.");

        var response = await Student.GetAsync("/api/gym/me/recovery-credits");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var student = await Factory.WithDbAsync(db => db.Students.AsNoTracking()
            .SingleAsync(item => item.Id == studentId));
        Assert.Null(student.IdentityId);
        Assert.Null(student.Dni);
        Assert.Single(Factory.Authcraft.Calls, call => call.Operation == "accepted-profile");
    }

    [Fact]
    public async Task Inactive_students_keep_staff_recovery_history_but_cannot_use_personal_recovery_routes()
    {
        var studentId = (await CreateStudentAsync(email: StudentEmail))
            .GetProperty("id").GetInt32();
        await Teacher.PostAsync($"/api/gym/students/{studentId}/invitations", null);
        Factory.Authcraft.AcceptedProfile = new AcceptedStudentProfile(
            100,
            studentId,
            "student-1",
            DateTimeOffset.UtcNow,
            new StudentProfileSnapshot(null, null, "Ana Pérez", "1155550000", null),
            new StudentRegisteredProfile("Ana", "Pérez", "1155550000", "01234567"));
        Assert.Equal(HttpStatusCode.OK,
            (await Student.GetAsync("/api/gym/me/recovery-credits")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Teacher.PostAsync($"/api/gym/students/{studentId}/deactivate", null)).StatusCode);

        var personal = await Student.GetAsync("/api/gym/me/recovery-credits");
        Assert.Equal(HttpStatusCode.NotFound, personal.StatusCode);
        Assert.Equal("no-store", personal.Headers.CacheControl?.ToString());

        var staffHistory = await Teacher.GetAsync(
            $"/api/gym/students/{studentId}/recovery-reservations?period=2026-11");
        Assert.Equal(HttpStatusCode.OK, staffHistory.StatusCode);
        Assert.Equal("no-store", staffHistory.Headers.CacheControl?.ToString());
        var history = await ReadAsync(staffHistory);
        Assert.Empty(history.EnumerateArray());
    }

    [Fact]
    public async Task Unlinked_foreign_and_kiosk_sessions_cannot_access_personal_recoveries()
    {
        using var foreignStudent = Factory.CreateClientFor("user", "foreign-student");

        var foreign = await foreignStudent.GetAsync("/api/gym/me/recovery-credits");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("no-store", foreign.Headers.CacheControl?.ToString());

        using var kioskRequest = new HttpRequestMessage(HttpMethod.Get, "/api/gym/me/recovery-credits");
        kioskRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Kiosk",
            "opaque-kiosk-token");
        var kiosk = await Anonymous.SendAsync(kioskRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, kiosk.StatusCode);
    }

    private async Task<int> CreateRecoveryCreditAsync(
        string email = StudentEmail,
        HttpClient? studentClient = null,
        string dni = "01234567")
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planResponse = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Recovery {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        Assert.Equal(HttpStatusCode.Created, planResponse.StatusCode);
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();
        var studentResponse = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email,
            phone = "1155550000",
            dni,
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, studentResponse.StatusCode);
        foreach (var day in new[] { 2, 3 })
        {
            Factory.Clock.SetBuenosAiresDate(2026, 11, day);
            Assert.Equal(HttpStatusCode.Created, (await (studentClient ?? Student).PostAsJsonAsync(
                "/api/gym/me/attendance/check-in", new { dni })).StatusCode);
        }

        Factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var credits = await (studentClient ?? Student).GetAsync("/api/gym/me/recovery-credits");
        Assert.Equal(HttpStatusCode.OK, credits.StatusCode);
        return (await ReadAsync(credits)).GetProperty("credits").EnumerateArray().Single()
            .GetProperty("id").GetInt32();
    }

    private async Task<int> CreateShiftAsync(string date)
    {
        var response = await Teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = date,
            starts_at = "18:00"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetInt32();
    }
}
