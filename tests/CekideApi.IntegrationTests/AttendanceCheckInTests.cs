using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.Domain;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public sealed class AttendanceCheckInTests : ApiTestBase
{
    [Fact]
    public async Task Self_check_in_is_created_once_and_does_not_expose_student_personal_data()
    {
        var planId = await CreatePlanWithLimitsAsync();
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "student-1@example.com",
            dni = "12.345.678",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        response = await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var result = await ReadAsync(response);
        Assert.Equal("created", result.GetProperty("outcome").GetString());
        Assert.Equal("self", result.GetProperty("attendance").GetProperty("source").GetString());
        Assert.False(result.GetProperty("attendance").TryGetProperty("dni", out _));
        Assert.False(result.TryGetProperty("email", out _));
        Assert.False(result.TryGetProperty("full_name", out _));

        response = await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        result = await ReadAsync(response);
        Assert.Equal("already_checked_in", result.GetProperty("outcome").GetString());
        Assert.Equal(1, result.GetProperty("attendance").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Self_check_in_rejects_another_students_dni_and_inactive_students()
    {
        var planId = await CreatePlanWithLimitsAsync();
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "student-1@example.com",
            dni = "12345678",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var studentId = (await ReadAsync(response)).GetProperty("id").GetInt32();

        response = await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "87654321" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        response = await Teacher.PostAsync($"/api/gym/students/{studentId}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        response = await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("student_inactive", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Check_in_accepts_excess_attendance_without_a_shift_or_reservation()
    {
        var planId = await CreatePlanWithLimitsAsync(weeklyDays: 3, monthlyDays: 1);
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "student-1@example.com",
            dni = "12345678",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        response = await Student.PostAsJsonAsync(
            "/api/gym/me/attendance/check-in",
            new { dni = "12345678", shift_id = (int?)null, credit_id = (int?)null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        response = await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await ReadAsync(response);
        Assert.True(result.GetProperty("attendance").GetProperty("monthly_exhausted").GetBoolean());
        Assert.Equal(1, result.GetProperty("summary").GetProperty("month").GetProperty("consumed").GetInt32());

        Factory.Clock.SetBuenosAiresDate(2026, 11, 6);
        response = await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        result = await ReadAsync(response);
        Assert.True(result.GetProperty("attendance").GetProperty("monthly_exceeded").GetBoolean());
        Assert.Equal(2, result.GetProperty("summary").GetProperty("month").GetProperty("consumed").GetInt32());
    }

    [Fact]
    public async Task Student_without_a_monthly_plan_cannot_check_in()
    {
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "student-1@example.com",
            dni = "12345678"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        response = await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("monthly_plan_required", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Missing_monthly_payment_does_not_block_attendance()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 12);
        var planId = await CreatePlanWithLimitsAsync();
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "student-1@example.com",
            dni = "12345678",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        response = await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("created", (await ReadAsync(response)).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task First_check_in_after_invited_signup_syncs_a_previously_missing_dni()
    {
        var planId = await CreatePlanWithLimitsAsync();
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "student-1@example.com",
            dni = (string?)null,
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var studentId = (await ReadAsync(response)).GetProperty("id").GetInt32();

        response = await Teacher.PostAsync($"/api/gym/students/{studentId}/invitations", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var invitation = await Factory.WithDbAsync(db =>
            db.StudentInvitations.AsNoTracking().SingleAsync(item => item.StudentId == studentId));
        Factory.Authcraft.AcceptedProfile = new AcceptedStudentProfile(
            invitation.ExternalInvitationId,
            studentId,
            "student-1",
            Factory.Clock.UtcNow,
            new StudentProfileSnapshot(null, null, "Ana Pérez", "1155550000", null),
            new StudentRegisteredProfile("Ana", "Pérez", "1155550000", "01234567"));

        response = await Student.PostAsJsonAsync(
            "/api/gym/me/attendance/check-in",
            new { dni = "01234567" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await ReadAsync(response);
        Assert.Equal("created", result.GetProperty("outcome").GetString());
        Assert.Equal("01234567", await Factory.WithDbAsync(async db =>
            (await db.Students.AsNoTracking().SingleAsync(item => item.Id == studentId)).Dni));
        Assert.Equal("student-1", await Factory.WithDbAsync(async db =>
            (await db.Students.AsNoTracking().SingleAsync(item => item.Id == studentId)).IdentityId));
        Assert.Single(Factory.Authcraft.Calls, call => call.Operation == "accepted-profile");
    }

    [Fact]
    public async Task Three_of_twelve_plan_warns_on_the_fourth_weekday_and_allows_thirteenth_day()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planId = await CreatePlanWithLimitsAsync(weeklyDays: 3, monthlyDays: 12);
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "student-1@example.com",
            dni = "01234567",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        JsonElement thursdayResult = default;
        JsonElement thirteenthResult = default;
        for (var day = 2; day <= 14; day++)
        {
            Factory.Clock.SetBuenosAiresDate(2026, 11, day);
            response = await Student.PostAsJsonAsync(
                "/api/gym/me/attendance/check-in",
                new { dni = "01234567" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var result = await ReadAsync(response);
            if (day == 5)
            {
                thursdayResult = result;
            }

            if (day == 14)
            {
                thirteenthResult = result;
            }
        }

        var thursday = thursdayResult;
        Assert.Equal("2026-11-05", thursday.GetProperty("attendance").GetProperty("local_date").GetString());
        Assert.True(thursday.GetProperty("attendance").GetProperty("weekly_exceeded").GetBoolean());
        Assert.Equal(4, thursday.GetProperty("summary").GetProperty("week").GetProperty("consumed").GetInt32());
        Assert.Equal(3, thursday.GetProperty("summary").GetProperty("week").GetProperty("limit").GetInt32());
        Assert.Equal(8, thursday.GetProperty("summary").GetProperty("month").GetProperty("remaining").GetInt32());
        Assert.Contains(
            thursday.GetProperty("warnings").EnumerateArray().Select(value => value.GetString()),
            warning => warning == "weekly_limit_exceeded");

        var thirteenth = thirteenthResult;
        Assert.Equal("2026-11-14", thirteenth.GetProperty("attendance").GetProperty("local_date").GetString());
        Assert.True(thirteenth.GetProperty("attendance").GetProperty("monthly_exceeded").GetBoolean());
        Assert.Equal(13, thirteenth.GetProperty("summary").GetProperty("month").GetProperty("consumed").GetInt32());
        Assert.Equal(0, thirteenth.GetProperty("summary").GetProperty("month").GetProperty("remaining").GetInt32());
        Assert.Equal(1, thirteenth.GetProperty("summary").GetProperty("month").GetProperty("exceeded").GetInt32());
        Assert.Contains(
            thirteenth.GetProperty("warnings").EnumerateArray().Select(value => value.GetString()),
            warning => warning == "monthly_quota_exhausted");
        Assert.Equal(1, await Factory.WithDbAsync(db => db.AttendanceQuotaNotifications.CountAsync()));
    }

    private async Task<int> CreatePlanWithLimitsAsync(int weeklyDays = 3, int monthlyDays = 12)
    {
        var response = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = "Attendance",
            monthly_price = 100m,
            weekly_days = weeklyDays,
            monthly_days = monthlyDays
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetInt32();
    }
}
