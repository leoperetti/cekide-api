using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public class NotificationsTests : ApiTestBase
{
    private async Task<int> OverdueStudentAsync(string name = "Ana Pérez", string email = "ana@example.com")
    {
        // The monthly membership starts in October, so November is overdue from day 11.
        Factory.Clock.SetBuenosAiresDate(2026, 10, 1);
        var plan = await CreatePlanAsync("Básico " + email, 15000m);
        var student = await CreateStudentAsync(name, email, plan.GetProperty("id").GetInt32());
        Factory.Clock.SetBuenosAiresDate(2026, 11, 12);
        return student.GetProperty("id").GetInt32();
    }

    private async Task<JsonElement> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/gym/notifications" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    [Fact]
    public async Task Staff_lists_the_sent_and_failed_notifications_with_student_data()
    {
        var anaId = await OverdueStudentAsync("Ana Pérez", "ana@example.com");
        await OverdueStudentAsync("Beto Paz", "beto@example.com");
        Factory.Emails.FailWith = "Brevo error 500";
        await Factory.RunOverdueNotificationsAsync();
        Factory.Emails.FailWith = null;
        await Factory.RunOverdueNotificationsAsync();
        Factory.Emails.FailWith = "Brevo error 500";
        await OverdueStudentAsync("Carla Gómez", "carla@example.com");
        await Factory.RunOverdueNotificationsAsync();

        foreach (var client in new[] { Owner, Teacher })
        {
            var all = await ListAsync(client);
            Assert.Equal(3, all.GetArrayLength());
            var ana = all.EnumerateArray().Single(n => n.GetProperty("student_id").GetInt32() == anaId);
            Assert.True(ana.GetProperty("id").GetInt32() > 0);
            Assert.Equal("Ana Pérez", ana.GetProperty("student_name").GetString());
            Assert.Equal("ana@example.com", ana.GetProperty("student_email").GetString());
            Assert.Equal("2026-11", ana.GetProperty("period").GetString());
            Assert.Equal("sent", ana.GetProperty("status").GetString());
            Assert.Equal(2, ana.GetProperty("attempts").GetInt32());
            Assert.Equal(JsonValueKind.String, ana.GetProperty("sent_at").ValueKind);
            Assert.Equal(JsonValueKind.Null, ana.GetProperty("last_error").ValueKind);
        }

        var failed = await ListAsync(Teacher, "?status=failed");
        var carla = Assert.Single(failed.EnumerateArray());
        Assert.Equal("Carla Gómez", carla.GetProperty("student_name").GetString());
        Assert.Equal("failed", carla.GetProperty("status").GetString());
        Assert.Equal(1, carla.GetProperty("attempts").GetInt32());
        Assert.Equal("Brevo error 500", carla.GetProperty("last_error").GetString());
        Assert.Equal(JsonValueKind.Null, carla.GetProperty("sent_at").ValueKind);

        Assert.Equal(2, (await ListAsync(Owner, "?status=sent")).GetArrayLength());
        Assert.Equal(3, (await ListAsync(Owner, "?period=2026-11")).GetArrayLength());
        Assert.Equal(0, (await ListAsync(Owner, "?period=2026-10")).GetArrayLength());
    }

    [Fact]
    public async Task An_invalid_filter_is_rejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Owner.GetAsync("/api/gym/notifications?period=11-2026")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Owner.GetAsync("/api/gym/notifications?status=bogus")).StatusCode);
    }

    [Fact]
    public async Task Staff_retries_a_failed_notification()
    {
        await OverdueStudentAsync();
        Factory.Emails.FailWith = "Brevo error 500";
        await Factory.RunOverdueNotificationsAsync();
        var id = (await ListAsync(Owner)).EnumerateArray().Single().GetProperty("id").GetInt32();
        Factory.Emails.FailWith = null;

        var response = await Teacher.PostAsync($"/api/gym/notifications/{id}/retry", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(id, body.GetProperty("id").GetInt32());
        Assert.Equal("sent", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("attempts").GetInt32());
        var message = Assert.Single(Factory.Emails.Sent);
        Assert.Equal("ana@example.com", message.ToEmail);
        Assert.Equal("sent", (await ListAsync(Owner)).EnumerateArray().Single().GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_retry_that_fails_again_keeps_the_notification_failed()
    {
        await OverdueStudentAsync();
        Factory.Emails.FailWith = "Brevo error 500";
        await Factory.RunOverdueNotificationsAsync();
        var id = (await ListAsync(Owner)).EnumerateArray().Single().GetProperty("id").GetInt32();

        var response = await Owner.PostAsync($"/api/gym/notifications/{id}/retry", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("failed", body.GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task Retrying_a_sent_notification_is_a_conflict()
    {
        await OverdueStudentAsync();
        await Factory.RunOverdueNotificationsAsync();
        var id = (await ListAsync(Owner)).EnumerateArray().Single().GetProperty("id").GetInt32();

        var response = await Owner.PostAsync($"/api/gym/notifications/{id}/retry", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(Factory.Emails.Sent);
    }

    [Fact]
    public async Task Retrying_a_missing_notification_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Owner.PostAsync("/api/gym/notifications/999/retry", null)).StatusCode);
    }

    [Fact]
    public async Task A_user_with_role_user_cannot_list_or_retry_notifications()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync("/api/gym/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.PostAsync("/api/gym/notifications/1/retry", null)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_are_unauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync("/api/gym/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.PostAsync("/api/gym/notifications/1/retry", null)).StatusCode);
    }

    [Fact]
    public async Task A_paid_student_never_gets_a_notice()
    {
        var studentId = await OverdueStudentAsync();
        var pay = await Teacher.PostAsJsonAsync($"/api/gym/students/{studentId}/payments", new { period = "2026-11" });
        Assert.Equal(HttpStatusCode.Created, pay.StatusCode);

        await Factory.RunOverdueNotificationsAsync();

        Assert.Equal(0, (await ListAsync(Owner)).GetArrayLength());
        Assert.Empty(Factory.Emails.Sent);
    }
}
