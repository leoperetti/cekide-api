using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public sealed class StudentAttendanceTests : ApiTestBase
{
    [Fact]
    public async Task Owner_and_teacher_read_the_same_history_for_active_and_inactive_students()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var studentId = await CreateStudentWithPlanAsync();
        Assert.Equal(HttpStatusCode.Created,
            (await Student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "01234567" })).StatusCode);
        var mine = await Student.GetAsync("/api/gym/me/attendance?period=2026-11");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var mineBody = await ReadAsync(mine);

        foreach (var client in new[] { Owner, Teacher })
        {
            var response = await client.GetAsync($"/api/gym/students/{studentId}/attendance?period=2026-11");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            var body = await ReadAsync(response);
            Assert.Equal(mineBody.GetProperty("attendances").GetRawText(), body.GetProperty("attendances").GetRawText());
            Assert.Equal(mineBody.GetProperty("period_summary").GetRawText(), body.GetProperty("period_summary").GetRawText());
            Assert.Equal(mineBody.GetProperty("current_summary").GetRawText(), body.GetProperty("current_summary").GetRawText());
        }

        Assert.Equal(HttpStatusCode.NoContent,
            (await Teacher.PostAsync($"/api/gym/students/{studentId}/deactivate", null)).StatusCode);
        var inactive = await Teacher.GetAsync($"/api/gym/students/{studentId}/attendance?period=2026-11");
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        var inactiveBody = await ReadAsync(inactive);
        Assert.Equal(mineBody.GetProperty("attendances").GetRawText(),
            inactiveBody.GetProperty("attendances").GetRawText());
        Assert.Equal(mineBody.GetProperty("period_summary").GetRawText(),
            inactiveBody.GetProperty("period_summary").GetRawText());
        Assert.Equal(JsonValueKind.Null, inactiveBody.GetProperty("current_summary").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Student.GetAsync("/api/gym/me/attendance?period=2026-11")).StatusCode);
    }

    [Fact]
    public async Task Staff_calendar_rejects_missing_students_students_from_other_tenants_and_student_roles()
    {
        var studentId = await CreateStudentWithPlanAsync();
        var missing = await Owner.GetAsync("/api/gym/students/999999/attendance?period=2026-11");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var foreignTeacher = Factory.CreateClientFor("teacher", tenant: "other-app");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await foreignTeacher.GetAsync($"/api/gym/students/{studentId}/attendance")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Student.GetAsync($"/api/gym/students/{studentId}/attendance")).StatusCode);
    }

    private async Task<int> CreateStudentWithPlanAsync()
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
        return (await ReadAsync(studentResponse)).GetProperty("id").GetInt32();
    }
}
