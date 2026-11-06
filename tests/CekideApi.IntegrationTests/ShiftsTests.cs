using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.Domain;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public sealed class ShiftsTests : ApiTestBase
{
    private static string Period(DateOnly date) => $"{date:yyyy-MM}";

    [Fact]
    public async Task Staff_can_create_list_cancel_and_query_shift_occupancy()
    {
        var studentId = await CreateStudentIdAsync();
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var response = await Teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = "2026-11-03",
            starts_at = "18:00"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var shift = await ReadAsync(response);
        var shiftId = shift.GetProperty("id").GetInt32();
        Assert.Equal("19:00:00", shift.GetProperty("ends_at").GetString());
        Assert.Equal(12, shift.GetProperty("capacity").GetInt32());

        response = await Teacher.PostAsJsonAsync(
            $"/api/gym/shifts/{shiftId}/habitual-occupancies",
            new { student_id = studentId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var occupancy = await ReadAsync(response);

        response = await Teacher.GetAsync($"/api/gym/shifts/{shiftId}/occupancy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var details = await ReadAsync(response);
        Assert.Equal(1, details.GetProperty("planned_count").GetInt32());
        Assert.Equal(1, details.GetProperty("habitual_count").GetInt32());
        Assert.Equal(0, details.GetProperty("reserved_count").GetInt32());
        Assert.Equal(0, details.GetProperty("actual_attended").GetInt32());
        Assert.Equal(11, details.GetProperty("available").GetInt32());
        Assert.Equal(studentId, details.GetProperty("habitual_occupancies")[0].GetProperty("student_id").GetInt32());

        response = await Teacher.PostAsync($"/api/gym/shifts/{shiftId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("cancelled", (await ReadAsync(response)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK,
            (await Teacher.PostAsync($"/api/gym/shifts/{shiftId}/cancel", null)).StatusCode);

        response = await Teacher.PostAsync(
            $"/api/gym/shifts/{shiftId}/habitual-occupancies/{occupancy.GetProperty("id").GetInt32()}/remove",
            null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("removed", (await ReadAsync(response)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK,
            (await Teacher.PostAsync(
                $"/api/gym/shifts/{shiftId}/habitual-occupancies/{occupancy.GetProperty("id").GetInt32()}/remove",
                null)).StatusCode);

        response = await Teacher.GetAsync($"/api/gym/shifts?period={Period(new DateOnly(2026, 11, 3))}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(1, (await ReadAsync(response)).GetArrayLength());
    }

    [Fact]
    public async Task Shift_creation_rejects_overlap_weekend_past_and_duplicate_students()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var response = await Teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = "2026-11-03",
            starts_at = "18:00"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        response = await Teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = "2026-11-03",
            starts_at = "18:30"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("shift_overlap", (await ReadAsync(response)).GetProperty("code").GetString());

        response = await Teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = "2026-11-07",
            starts_at = "18:00"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        response = await Teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = "2026-10-30",
            starts_at = "18:00"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var studentId = await CreateStudentIdAsync();
        var shift = (await ReadAsync(await Teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = "2026-11-04",
            starts_at = "18:00"
        }))).GetProperty("id").GetInt32();
        var first = await Teacher.PostAsJsonAsync(
            $"/api/gym/shifts/{shift}/habitual-occupancies", new { student_id = studentId });
        var duplicate = await Teacher.PostAsJsonAsync(
            $"/api/gym/shifts/{shift}/habitual-occupancies", new { student_id = studentId });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
    }

    [Fact]
    public async Task Only_staff_can_manage_shifts_and_kiosk_cannot_query_them()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        using var kiosk = Factory.CreateClient();
        var invalidToken = await Teacher.PostAsJsonAsync("/api/gym/kiosk-sessions", new { label = "Entrada" });
        Assert.Equal(HttpStatusCode.Created, invalidToken.StatusCode);
        var activationCode = (await ReadAsync(invalidToken)).GetProperty("activation_code").GetString();
        var activated = await kiosk.PostAsJsonAsync("/api/gym/kiosk/activate", new { activation_code = activationCode });
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var token = (await ReadAsync(activated)).GetProperty("token").GetString();
        kiosk.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Kiosk", token);

        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync("/api/gym/shifts?period=2026-11")).StatusCode);
        var ownerResponse = await Owner.PostAsJsonAsync(
            "/api/gym/shifts", new { local_date = "2026-11-03", starts_at = "18:00" });
        Assert.Equal(HttpStatusCode.Created, ownerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kiosk.GetAsync("/api/gym/shifts?period=2026-11")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await kiosk.PostAsJsonAsync(
            "/api/gym/shifts", new { local_date = "2026-11-03", starts_at = "19:00" })).StatusCode);
    }

    [Fact]
    public async Task Check_ins_do_not_use_shift_capacity_and_actual_attendance_can_exceed_twelve()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planId = await CreatePlanWithLimitsAsync();
        var shiftResponse = await Teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = "2026-11-03",
            starts_at = "18:00"
        });
        Assert.Equal(HttpStatusCode.Created, shiftResponse.StatusCode);
        var shiftId = (await ReadAsync(shiftResponse)).GetProperty("id").GetInt32();

        var students = new List<(int Id, string Dni)>();
        for (var index = 0; index < 14; index++)
        {
            var dni = (20_000_000 + index).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
            {
                full_name = $"Alumno {index}",
                email = $"student-{index}@example.com",
                dni,
                plan_id = planId
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            students.Add(((await ReadAsync(response)).GetProperty("id").GetInt32(), dni));

            if (index < 12)
            {
                var planned = await Teacher.PostAsJsonAsync(
                    $"/api/gym/shifts/{shiftId}/habitual-occupancies",
                    new { student_id = students[index].Id });
                Assert.Equal(HttpStatusCode.Created, planned.StatusCode);
            }
        }

        Factory.Clock.Set(new DateTimeOffset(2026, 11, 3, 21, 0, 0, TimeSpan.Zero));
        foreach (var (_, dni) in students)
        {
            using var client = Factory.CreateClientFor("user", $"student-{students.FindIndex(s => s.Dni == dni)}");
            var response = await client.PostAsJsonAsync(
                "/api/gym/me/attendance/check-in",
                new { dni });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var occupancy = await ReadAsync(await Teacher.GetAsync($"/api/gym/shifts/{shiftId}/occupancy"));
        Assert.Equal(12, occupancy.GetProperty("planned_count").GetInt32());
        Assert.Equal(12, occupancy.GetProperty("habitual_count").GetInt32());
        Assert.Equal(14, occupancy.GetProperty("actual_attended").GetInt32());
        Assert.Equal(14, occupancy.GetProperty("attendees").GetArrayLength());
    }

    private async Task<int> CreateStudentIdAsync()
    {
        var planId = await CreatePlanWithLimitsAsync();
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = $"student-{Guid.NewGuid():N}@example.com",
            dni = Random.Shared.Next(10_000_000, 99_999_999).ToString(),
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetInt32();
    }

    private async Task<int> CreatePlanWithLimitsAsync()
    {
        var response = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Shift plan {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 5,
            monthly_days = 25
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetInt32();
    }
}
