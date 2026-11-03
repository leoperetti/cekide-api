using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public class StudentPaymentsTests : ApiTestBase
{
    private static int Id(JsonElement element) => element.GetProperty("id").GetInt32();

    private async Task<int> MonthlyStudentAsync(decimal price = 15000m, string planName = "Básico", string email = "ana@example.com")
    {
        var plan = await CreatePlanAsync(planName, price);
        return Id(await CreateStudentAsync(email: email, planId: Id(plan)));
    }

    private Task SetTariffsAsync() => Owner.PutAsJsonAsync("/api/gym/pass-tariffs", new[]
    {
        new { type = "day", price = 3000m },
        new { type = "week", price = 12000m },
        new { type = "fifteen_days", price = 20000m }
    });

    private Task<HttpResponseMessage> PayMonthAsync(int studentId, string period, decimal? amount = null, HttpClient? client = null) =>
        (client ?? Teacher).PostAsJsonAsync($"/api/gym/students/{studentId}/payments", new { period, amount });

    private Task<HttpResponseMessage> BuyPassAsync(int studentId, string type, string? validFrom = null, decimal? amount = null, HttpClient? client = null) =>
        (client ?? Teacher).PostAsJsonAsync($"/api/gym/students/{studentId}/payments", new { type, valid_from = validFrom, amount });

    [Fact]
    public async Task Teacher_registers_a_monthly_payment()
    {
        var studentId = await MonthlyStudentAsync();

        var response = await PayMonthAsync(studentId, "2026-11", 15000m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await ReadAsync(response);
        Assert.True(Id(payment) > 0);
        Assert.Equal("monthly", payment.GetProperty("kind").GetString());
        Assert.Equal(studentId, payment.GetProperty("student_id").GetInt32());
        Assert.Equal("2026-11", payment.GetProperty("period").GetString());
        Assert.Equal(15000m, payment.GetProperty("amount").GetDecimal());
        Assert.Equal("paid", payment.GetProperty("status").GetString());
        Assert.Equal("2026-11-05", payment.GetProperty("paid_on").GetString());
        Assert.Equal("teacher-1", payment.GetProperty("registered_by").GetString());
    }

    [Fact]
    public async Task Amount_defaults_to_the_plan_price_and_paid_on_can_be_given()
    {
        var studentId = await MonthlyStudentAsync(15000m);

        var response = await Teacher.PostAsJsonAsync(
            $"/api/gym/students/{studentId}/payments", new { period = "2026-11", paid_on = "2026-11-03" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await ReadAsync(response);
        Assert.Equal(15000m, payment.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-11-03", payment.GetProperty("paid_on").GetString());
    }

    [Theory]
    [InlineData(7500)]
    [InlineData(20000)]
    [InlineData(14999.99)]
    public async Task An_amount_different_from_the_plan_price_is_rejected_because_there_are_no_partial_payments(double amount)
    {
        var studentId = await MonthlyStudentAsync(15000m);

        var response = await PayMonthAsync(studentId, "2026-11", (decimal)amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_second_paid_payment_for_the_same_student_and_period_is_a_conflict()
    {
        var studentId = await MonthlyStudentAsync();
        await PayMonthAsync(studentId, "2026-11");

        var duplicate = await PayMonthAsync(studentId, "2026-11");
        var otherMonth = await PayMonthAsync(studentId, "2026-12");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherMonth.StatusCode);
    }

    [Fact]
    public async Task A_voided_period_can_be_paid_again()
    {
        var studentId = await MonthlyStudentAsync();
        var first = await ReadAsync(await PayMonthAsync(studentId, "2026-11"));
        Assert.Equal(HttpStatusCode.NoContent, (await Owner.DeleteAsync($"/api/gym/payments/{Id(first)}")).StatusCode);

        var again = await PayMonthAsync(studentId, "2026-11");

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("nov-2026")]
    public async Task An_invalid_period_is_rejected(string period)
    {
        var studentId = await MonthlyStudentAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PayMonthAsync(studentId, period)).StatusCode);
    }

    [Fact]
    public async Task A_period_before_the_monthly_membership_started_is_rejected()
    {
        var studentId = await MonthlyStudentAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PayMonthAsync(studentId, "2026-10")).StatusCode);
    }

    [Fact]
    public async Task A_student_without_a_monthly_plan_cannot_pay_a_month()
    {
        var student = await CreateStudentAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await PayMonthAsync(Id(student), "2026-11")).StatusCode);
    }

    [Fact]
    public async Task Inactive_students_cannot_register_new_payments_but_keep_their_history()
    {
        await SetTariffsAsync();
        var studentId = await MonthlyStudentAsync();
        await PayMonthAsync(studentId, "2026-11");
        await Teacher.PostAsync($"/api/gym/students/{studentId}/deactivate", null);

        Assert.Equal(HttpStatusCode.BadRequest, (await PayMonthAsync(studentId, "2026-12")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await BuyPassAsync(studentId, "day")).StatusCode);

        var list = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}/payments"));
        Assert.Single(list.EnumerateArray());
    }

    [Fact]
    public async Task Price_changes_do_not_alter_registered_payments_and_apply_to_new_ones()
    {
        var plan = await CreatePlanAsync("Básico", 15000m);
        var studentId = Id(await CreateStudentAsync(planId: Id(plan)));
        await PayMonthAsync(studentId, "2026-11", 15000m);

        await Owner.PutAsJsonAsync($"/api/gym/plans/{Id(plan)}", new { name = "Básico", monthly_price = 20000m });

        var list = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}/payments"));
        Assert.Equal(15000m, list.EnumerateArray().Single().GetProperty("amount").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await PayMonthAsync(studentId, "2026-12", 15000m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PayMonthAsync(studentId, "2026-12", 20000m)).StatusCode);
    }

    [Fact]
    public async Task A_plan_change_applies_to_the_months_after_the_current_one()
    {
        var basic = await CreatePlanAsync("Básico", 15000m);
        var premium = await CreatePlanAsync("Premium", 25000m);
        var studentId = Id(await CreateStudentAsync(planId: Id(basic)));
        await Teacher.PutAsJsonAsync($"/api/gym/students/{studentId}",
            new { full_name = "Ana Pérez", email = "ana@example.com", plan_id = Id(premium) });

        Assert.Equal(HttpStatusCode.BadRequest, (await PayMonthAsync(studentId, "2026-11", 25000m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PayMonthAsync(studentId, "2026-11", 15000m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PayMonthAsync(studentId, "2026-12", 15000m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PayMonthAsync(studentId, "2026-12", 25000m)).StatusCode);
    }

    [Theory]
    [InlineData("day", "2026-11-10", "2026-11-10", 3000)]
    [InlineData("week", "2026-11-10", "2026-11-16", 12000)]
    [InlineData("fifteen_days", "2026-11-10", "2026-11-24", 20000)]
    public async Task Teacher_registers_short_term_passes_with_their_validity(string type, string from, string until, int price)
    {
        await SetTariffsAsync();
        var student = await CreateStudentAsync();

        var response = await BuyPassAsync(Id(student), type, from, price);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var pass = await ReadAsync(response);
        Assert.Equal("pass", pass.GetProperty("kind").GetString());
        Assert.Equal(type, pass.GetProperty("type").GetString());
        Assert.Equal(from, pass.GetProperty("valid_from").GetString());
        Assert.Equal(until, pass.GetProperty("valid_until").GetString());
        Assert.Equal(price, pass.GetProperty("amount").GetDecimal());
        Assert.Equal("paid", pass.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_pass_defaults_to_today_and_to_the_tariff_price()
    {
        await SetTariffsAsync();
        var student = await CreateStudentAsync();

        var pass = await ReadAsync(await BuyPassAsync(Id(student), "week"));

        Assert.Equal("2026-11-05", pass.GetProperty("valid_from").GetString());
        Assert.Equal("2026-11-11", pass.GetProperty("valid_until").GetString());
        Assert.Equal(12000m, pass.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task A_pass_amount_must_match_the_tariff_and_the_tariff_must_exist()
    {
        var student = await CreateStudentAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await BuyPassAsync(Id(student), "day")).StatusCode);

        await SetTariffsAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await BuyPassAsync(Id(student), "day", amount: 1000m)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await BuyPassAsync(Id(student), "day", amount: 3000m)).StatusCode);
    }

    [Fact]
    public async Task Passes_can_be_bought_by_monthly_students_too_and_do_not_block_the_monthly_payment()
    {
        await SetTariffsAsync();
        var studentId = await MonthlyStudentAsync();

        Assert.Equal(HttpStatusCode.Created, (await BuyPassAsync(studentId, "day")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PayMonthAsync(studentId, "2026-11")).StatusCode);

        var list = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}/payments"));
        Assert.Equal(new[] { "monthly", "pass" }, list.EnumerateArray().Select(p => p.GetProperty("kind").GetString()!).Order());
    }

    [Fact]
    public async Task A_request_must_describe_either_a_month_or_a_pass()
    {
        var studentId = await MonthlyStudentAsync();

        var neither = await Teacher.PostAsJsonAsync($"/api/gym/students/{studentId}/payments", new { amount = 100m });
        var both = await Teacher.PostAsJsonAsync($"/api/gym/students/{studentId}/payments", new { period = "2026-11", type = "day" });

        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
    }

    [Fact]
    public async Task Owner_can_register_payments_too()
    {
        var studentId = await MonthlyStudentAsync();

        Assert.Equal(HttpStatusCode.Created, (await PayMonthAsync(studentId, "2026-11", client: Owner)).StatusCode);
    }

    [Fact]
    public async Task Listing_returns_monthly_payments_and_passes_of_the_student_only()
    {
        await SetTariffsAsync();
        var studentId = await MonthlyStudentAsync(email: "ana@example.com");
        var otherId = await MonthlyStudentAsync(planName: "Otro", email: "otro@example.com");
        await PayMonthAsync(studentId, "2026-11");
        await BuyPassAsync(studentId, "day");
        await PayMonthAsync(otherId, "2026-11");

        var list = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}/payments"));

        Assert.Equal(2, list.GetArrayLength());
        Assert.All(list.EnumerateArray(), p => Assert.Equal(studentId, p.GetProperty("student_id").GetInt32()));
    }

    [Fact]
    public async Task Students_role_cannot_read_or_register_payments_of_a_student()
    {
        var studentId = await MonthlyStudentAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync($"/api/gym/students/{studentId}/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PayMonthAsync(studentId, "2026-11", client: Student)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync($"/api/gym/students/{studentId}/payments")).StatusCode);
    }

    [Fact]
    public async Task Unknown_students_are_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Teacher.GetAsync("/api/gym/students/999/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PayMonthAsync(999, "2026-11")).StatusCode);
    }
}
