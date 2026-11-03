using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public class MyPaymentsTests : ApiTestBase
{
    // TestTokens emits the email claim as "<subject>@example.com", so the "student-1" client matches this student.
    private const string StudentEmail = "student-1@example.com";

    private static int Id(JsonElement element) => element.GetProperty("id").GetInt32();

    private Task<HttpResponseMessage> MineAsync(HttpClient? client = null) =>
        (client ?? Student).GetAsync("/api/gym/me/payments");

    private async Task<JsonElement> MineOkAsync(HttpClient? client = null)
    {
        var response = await MineAsync(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    /// <summary>A monthly student (15000) whose membership began on the given Buenos Aires day; the clock ends on Nov 5.</summary>
    private async Task<int> MonthlyStudentAsync(string email, int startMonth = 10, int startDay = 1, string planName = "Básico")
    {
        Factory.Clock.SetBuenosAiresDate(2026, startMonth, startDay);
        var plan = await CreatePlanAsync(planName, 15000m);
        var student = await CreateStudentAsync(email: email, planId: Id(plan));
        Factory.Clock.SetBuenosAiresDate(2026, 11, 5);
        return Id(student);
    }

    private Task<HttpResponseMessage> PayMonthAsync(int studentId, string period) =>
        Teacher.PostAsJsonAsync($"/api/gym/students/{studentId}/payments", new { period });

    [Fact]
    public async Task Returns_only_the_callers_payments_paid_and_unpaid_months_newest_first()
    {
        var mine = await MonthlyStudentAsync(StudentEmail);
        var other = await MonthlyStudentAsync("beto@example.com", planName: "Otro");
        await PayMonthAsync(mine, "2026-11");
        await PayMonthAsync(other, "2026-10");
        await PayMonthAsync(other, "2026-11");

        var payments = await MineOkAsync();

        var rows = payments.EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("2026-11", rows[0].GetProperty("period").GetString());
        Assert.Equal("monthly", rows[0].GetProperty("kind").GetString());
        Assert.Equal("paid", rows[0].GetProperty("status").GetString());
        Assert.Equal(15000m, rows[0].GetProperty("amount").GetDecimal());
        Assert.Equal("2026-11-05", rows[0].GetProperty("paid_on").GetString());
        Assert.True(Id(rows[0]) > 0);
        Assert.Equal("2026-10", rows[1].GetProperty("period").GetString());
        Assert.Equal("overdue", rows[1].GetProperty("status").GetString());
        Assert.Equal(15000m, rows[1].GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("paid_on").ValueKind);
        Assert.DoesNotContain(rows, row => row.TryGetProperty("student_id", out _));
    }

    [Fact]
    public async Task The_current_month_is_pending_inside_the_payment_window_and_overdue_after_day_10()
    {
        await MonthlyStudentAsync(StudentEmail, startMonth: 11, startDay: 1);

        var pending = (await MineOkAsync()).EnumerateArray().Single();
        Assert.Equal("2026-11", pending.GetProperty("period").GetString());
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        Assert.Equal(15000m, pending.GetProperty("amount").GetDecimal());

        Factory.Clock.SetBuenosAiresDate(2026, 11, 11);
        var overdue = (await MineOkAsync()).EnumerateArray().Single();
        Assert.Equal("overdue", overdue.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Includes_the_callers_short_term_passes()
    {
        await Owner.PutAsJsonAsync("/api/gym/pass-tariffs", new[]
        {
            new { type = "day", price = 3000m },
            new { type = "week", price = 12000m },
            new { type = "fifteen_days", price = 20000m }
        });
        var student = await CreateStudentAsync(email: StudentEmail);
        var other = await CreateStudentAsync(email: "beto@example.com");
        await Teacher.PostAsJsonAsync($"/api/gym/students/{Id(student)}/payments", new { type = "week", valid_from = "2026-11-03" });
        await Teacher.PostAsJsonAsync($"/api/gym/students/{Id(other)}/payments", new { type = "day" });

        var row = (await MineOkAsync()).EnumerateArray().Single();

        Assert.Equal("pass", row.GetProperty("kind").GetString());
        Assert.Equal("week", row.GetProperty("type").GetString());
        Assert.Equal("paid", row.GetProperty("status").GetString());
        Assert.Equal(12000m, row.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-11-03", row.GetProperty("valid_from").GetString());
        Assert.Equal("2026-11-09", row.GetProperty("valid_until").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("period").ValueKind);
    }

    [Fact]
    public async Task Voided_payments_are_not_listed_and_the_month_becomes_unpaid_again()
    {
        var studentId = await MonthlyStudentAsync(StudentEmail, startMonth: 11, startDay: 1);
        var payment = await ReadAsync(await PayMonthAsync(studentId, "2026-11"));
        await Owner.DeleteAsync($"/api/gym/payments/{Id(payment)}");

        var row = (await MineOkAsync()).EnumerateArray().Single();

        Assert.Equal("pending", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_student_with_nothing_to_pay_gets_an_empty_list()
    {
        await CreateStudentAsync(email: StudentEmail);

        Assert.Equal(0, (await MineOkAsync()).GetArrayLength());
    }

    [Fact]
    public async Task The_first_call_links_the_account_to_the_student_with_the_same_email()
    {
        var studentId = await MonthlyStudentAsync(StudentEmail);

        await MineOkAsync();

        var identity = await Factory.WithDbAsync(async db => (await db.Students.SingleAsync(s => s.Id == studentId)).IdentityId);
        Assert.Equal("student-1", identity);
        Assert.Equal(HttpStatusCode.OK, (await MineAsync()).StatusCode);
    }

    [Fact]
    public async Task An_account_without_a_matching_student_is_not_found()
    {
        await CreateStudentAsync(email: "beto@example.com");

        Assert.Equal(HttpStatusCode.NotFound, (await MineAsync()).StatusCode);
    }

    [Fact]
    public async Task An_account_cannot_take_over_a_student_linked_to_someone_else()
    {
        await CreateStudentAsync(email: "stranger-9@example.com");
        await Factory.WithDbAsync(async db =>
        {
            (await db.Students.SingleAsync()).LinkIdentity("someone-else");
            return await db.SaveChangesAsync();
        });
        using var stranger = Factory.CreateClientFor("user", "stranger-9");

        Assert.Equal(HttpStatusCode.NotFound, (await MineAsync(stranger)).StatusCode);
        var identity = await Factory.WithDbAsync(async db => (await db.Students.SingleAsync()).IdentityId);
        Assert.Equal("someone-else", identity);
    }

    [Fact]
    public async Task Owner_and_teacher_without_the_user_role_are_forbidden()
    {
        await CreateStudentAsync(email: "owner-1@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await MineAsync(Owner)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await MineAsync(Teacher)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_are_unauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await MineAsync(Anonymous)).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_student_is_forbidden_even_when_already_linked()
    {
        var studentId = await MonthlyStudentAsync(StudentEmail);
        await MineOkAsync();
        await Teacher.PostAsync($"/api/gym/students/{studentId}/deactivate", null);

        Assert.Equal(HttpStatusCode.Forbidden, (await MineAsync()).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_student_that_was_never_linked_is_forbidden_and_stays_unlinked()
    {
        var student = await CreateStudentAsync(email: StudentEmail);
        await Teacher.PostAsync($"/api/gym/students/{Id(student)}/deactivate", null);

        Assert.Equal(HttpStatusCode.Forbidden, (await MineAsync()).StatusCode);

        var identity = await Factory.WithDbAsync(async db => (await db.Students.SingleAsync()).IdentityId);
        Assert.Null(identity);
    }
}
