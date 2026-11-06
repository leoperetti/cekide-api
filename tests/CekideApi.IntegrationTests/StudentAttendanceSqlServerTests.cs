using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace CekideApi.IntegrationTests;

public sealed class StudentAttendanceSqlServerTests : IAsyncLifetime
{
    private readonly CekideApiFactory _factory;
    private readonly HttpClient _owner;
    private readonly HttpClient _teacher;
    private readonly HttpClient _student;

    public StudentAttendanceSqlServerTests()
    {
        var connection = new SqlConnectionStringBuilder(AttendanceSqlServerFixture.ValidateConnectionString(
            Environment.GetEnvironmentVariable(AttendanceSqlServerFixture.ConnectionVariable)))
        {
            InitialCatalog = $"cekide_attendance_test_staff_{Guid.NewGuid():N}"
        };
        _factory = new CekideApiFactory(connection.ConnectionString);
        _owner = _factory.CreateClientFor("owner", "owner-1");
        _teacher = _factory.CreateClientFor("teacher", "teacher-1");
        _student = _factory.CreateClientFor("user", "student-1");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _owner.Dispose();
        _teacher.Dispose();
        _student.Dispose();
        await _factory.WithDbAsync(async db =>
        {
            await db.Database.EnsureDeletedAsync();
            return true;
        });
        _factory.Dispose();
    }

    [Fact]
    public async Task Staff_calendar_matches_student_and_preserves_history_after_deactivation()
    {
        _factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var planResponse = await _owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Attendance {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        Assert.Equal(HttpStatusCode.Created, planResponse.StatusCode);
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();

        var studentResponse = await _teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "student-1@example.com",
            phone = "1155550000",
            dni = "01234567",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, studentResponse.StatusCode);
        var studentId = (await ReadAsync(studentResponse)).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.Created,
            (await _student.PostAsJsonAsync("/api/gym/me/attendance/check-in", new { dni = "01234567" })).StatusCode);
        var mine = await _student.GetAsync("/api/gym/me/attendance?period=2026-11");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var mineBody = await ReadAsync(mine);

        Assert.Equal(HttpStatusCode.NoContent,
            (await _teacher.PostAsync($"/api/gym/students/{studentId}/deactivate", null)).StatusCode);

        foreach (var client in new[] { _owner, _teacher })
        {
            var response = await client.GetAsync(
                $"/api/gym/students/{studentId}/attendance?period=2026-11");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            var body = await ReadAsync(response);
            Assert.Equal(mineBody.GetProperty("attendances").GetRawText(),
                body.GetProperty("attendances").GetRawText());
            Assert.Equal(mineBody.GetProperty("period_summary").GetRawText(),
                body.GetProperty("period_summary").GetRawText());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("current_summary").ValueKind);
        }

        Assert.Equal(HttpStatusCode.Conflict,
            (await _student.GetAsync("/api/gym/me/attendance?period=2026-11")).StatusCode);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
