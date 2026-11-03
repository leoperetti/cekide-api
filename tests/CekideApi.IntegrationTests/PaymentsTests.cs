using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public class PaymentsTests : ApiTestBase
{
    private static int Id(JsonElement element) => element.GetProperty("id").GetInt32();

    private async Task<int> MonthlyStudentAsync(string email = "ana@example.com", string name = "Ana Pérez", int? planId = null)
    {
        planId ??= Id(await CreatePlanAsync($"Plan {email}", 15000m));
        return Id(await CreateStudentAsync(name, email, planId));
    }

    private async Task<JsonElement> PayMonthAsync(int studentId, string period = "2026-11")
    {
        var response = await Teacher.PostAsJsonAsync($"/api/gym/students/{studentId}/payments", new { period });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<JsonElement> BuyPassAsync(int studentId)
    {
        await Owner.PutAsJsonAsync("/api/gym/pass-tariffs", new[] { new { type = "day", price = 3000m } });
        var response = await Teacher.PostAsJsonAsync($"/api/gym/students/{studentId}/payments", new { type = "day" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private async Task<JsonElement[]> RowsAsync(string query = "")
    {
        var response = await Owner.GetAsync($"/api/gym/payments{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync(response)).EnumerateArray().ToArray();
    }

    // ---------- Authorization ----------

    [Fact]
    public async Task Teacher_cannot_modify_void_list_or_read_history()
    {
        var payment = await PayMonthAsync(await MonthlyStudentAsync());
        var id = Id(payment);

        Assert.Equal(HttpStatusCode.Forbidden, (await Teacher.PutAsJsonAsync($"/api/gym/payments/{id}", new { amount = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Teacher.DeleteAsync($"/api/gym/payments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Teacher.GetAsync($"/api/gym/payments/{id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Teacher.GetAsync("/api/gym/payments")).StatusCode);
    }

    [Fact]
    public async Task Students_role_and_anonymous_cannot_reach_the_payments_endpoints()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync("/api/gym/payments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.DeleteAsync("/api/gym/payments/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync("/api/gym/payments")).StatusCode);
    }

    // ---------- Modify / void with history ----------

    [Fact]
    public async Task Owner_modifies_a_payment_and_the_change_is_recorded_in_the_history()
    {
        var payment = await PayMonthAsync(await MonthlyStudentAsync());
        var id = Id(payment);

        var response = await Owner.PutAsJsonAsync($"/api/gym/payments/{id}", new { amount = 16000m, paid_on = "2026-11-04" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadAsync(response);
        Assert.Equal(16000m, updated.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-11-04", updated.GetProperty("paid_on").GetString());

        var history = (await ReadAsync(await Owner.GetAsync($"/api/gym/payments/{id}/history"))).EnumerateArray().ToArray();
        Assert.Equal(new[] { "created", "modified" }, history.Select(h => h.GetProperty("action").GetString()));

        var created = history[0];
        Assert.Equal(JsonValueKind.Null, created.GetProperty("old_values").ValueKind);
        Assert.Equal(15000m, created.GetProperty("new_values").GetProperty("amount").GetDecimal());
        Assert.Equal("teacher-1", created.GetProperty("performed_by").GetString());

        var modified = history[1];
        Assert.Equal(15000m, modified.GetProperty("old_values").GetProperty("amount").GetDecimal());
        Assert.Equal("2026-11-05", modified.GetProperty("old_values").GetProperty("paid_on").GetString());
        Assert.Equal(16000m, modified.GetProperty("new_values").GetProperty("amount").GetDecimal());
        Assert.Equal("2026-11-04", modified.GetProperty("new_values").GetProperty("paid_on").GetString());
        Assert.Equal("owner-1", modified.GetProperty("performed_by").GetString());
        Assert.Equal(Factory.Clock.UtcNow.UtcDateTime, modified.GetProperty("performed_at").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task Owner_voids_a_payment_and_the_history_keeps_every_step()
    {
        var id = Id(await PayMonthAsync(await MonthlyStudentAsync()));

        var response = await Owner.DeleteAsync($"/api/gym/payments/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var history = (await ReadAsync(await Owner.GetAsync($"/api/gym/payments/{id}/history"))).EnumerateArray().ToArray();
        Assert.Equal(new[] { "created", "voided" }, history.Select(h => h.GetProperty("action").GetString()));
        Assert.Equal("paid", history[1].GetProperty("old_values").GetProperty("status").GetString());
        Assert.Equal("voided", history[1].GetProperty("new_values").GetProperty("status").GetString());
        Assert.Equal("owner-1", history[1].GetProperty("performed_by").GetString());
    }

    [Fact]
    public async Task A_voided_payment_cannot_be_voided_or_modified_again()
    {
        var id = Id(await PayMonthAsync(await MonthlyStudentAsync()));
        await Owner.DeleteAsync($"/api/gym/payments/{id}");

        Assert.Equal(HttpStatusCode.Conflict, (await Owner.DeleteAsync($"/api/gym/payments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Owner.PutAsJsonAsync($"/api/gym/payments/{id}", new { amount = 1m })).StatusCode);
    }

    [Fact]
    public async Task Invalid_modifications_are_rejected_without_writing_history()
    {
        var id = Id(await PayMonthAsync(await MonthlyStudentAsync()));

        Assert.Equal(HttpStatusCode.BadRequest, (await Owner.PutAsJsonAsync($"/api/gym/payments/{id}", new { amount = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Owner.PutAsJsonAsync($"/api/gym/payments/{id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Owner.PutAsJsonAsync($"/api/gym/payments/{id}", new { valid_from = "2026-11-01" })).StatusCode);

        var history = await ReadAsync(await Owner.GetAsync($"/api/gym/payments/{id}/history"));
        Assert.Equal(1, history.GetArrayLength());
    }

    [Fact]
    public async Task Unknown_payments_are_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Owner.PutAsJsonAsync("/api/gym/payments/999", new { amount = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Owner.DeleteAsync("/api/gym/payments/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Owner.GetAsync("/api/gym/payments/999/history")).StatusCode);
    }

    [Fact]
    public async Task Passes_are_modified_and_voided_through_kind_pass_with_their_own_history()
    {
        var studentId = await MonthlyStudentAsync();
        var pass = await BuyPassAsync(studentId);
        var id = Id(pass);

        var put = await Owner.PutAsJsonAsync($"/api/gym/payments/{id}?kind=pass", new { valid_from = "2026-11-06", valid_until = "2026-11-07" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await ReadAsync(put);
        Assert.Equal("2026-11-06", updated.GetProperty("valid_from").GetString());
        Assert.Equal("2026-11-07", updated.GetProperty("valid_until").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await Owner.DeleteAsync($"/api/gym/payments/{id}?kind=pass")).StatusCode);

        var history = (await ReadAsync(await Owner.GetAsync($"/api/gym/payments/{id}/history?kind=pass"))).EnumerateArray().ToArray();
        Assert.Equal(new[] { "created", "modified", "voided" }, history.Select(h => h.GetProperty("action").GetString()));
        Assert.Equal("2026-11-05", history[1].GetProperty("old_values").GetProperty("valid_from").GetString());
    }

    [Fact]
    public async Task Pass_and_monthly_ids_do_not_collide_because_kind_selects_the_table()
    {
        var studentId = await MonthlyStudentAsync();
        var month = await PayMonthAsync(studentId);
        var pass = await BuyPassAsync(studentId);
        Assert.Equal(Id(month), Id(pass));

        await Owner.DeleteAsync($"/api/gym/payments/{Id(pass)}?kind=pass");

        var list = (await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}/payments"))).EnumerateArray().ToArray();
        Assert.Equal("paid", list.Single(p => p.GetProperty("kind").GetString() == "monthly").GetProperty("status").GetString());
        Assert.Equal("voided", list.Single(p => p.GetProperty("kind").GetString() == "pass").GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_unknown_kind_is_rejected()
    {
        var id = Id(await PayMonthAsync(await MonthlyStudentAsync()));

        Assert.Equal(HttpStatusCode.BadRequest, (await Owner.DeleteAsync($"/api/gym/payments/{id}?kind=gift")).StatusCode);
    }

    // ---------- Payments screen (owner) ----------

    private async Task SeedOctoberAndNovemberAsync()
    {
        // paid / unpaid / voided students, all with monthly membership from before November.
        Factory.Clock.SetBuenosAiresDate(2026, 10, 1);
        var plan = Id(await CreatePlanAsync("Básico", 15000m));
        var paid = await MonthlyStudentAsync("paid@example.com", "Paula Paga", plan);
        var unpaid = await MonthlyStudentAsync("unpaid@example.com", "Ulises Debe", plan);
        var voided = await MonthlyStudentAsync("voided@example.com", "Vera Anulada", plan);
        var shortTerm = Id(await CreateStudentAsync("Pablo Pase", "pase@example.com"));
        var inactive = await MonthlyStudentAsync("inactive@example.com", "Ines Inactiva", plan);
        await Teacher.PostAsync($"/api/gym/students/{inactive}/deactivate", null);

        Factory.Clock.SetBuenosAiresDate(2026, 11, 3);
        await PayMonthAsync(paid);
        var voidedPayment = await PayMonthAsync(voided);
        await Owner.DeleteAsync($"/api/gym/payments/{Id(voidedPayment)}");
        await BuyPassAsync(shortTerm);
        _ = unpaid;
    }

    [Fact]
    public async Task Owner_filters_the_payments_screen_by_status_for_a_month()
    {
        await SeedOctoberAndNovemberAsync();
        Factory.Clock.SetBuenosAiresDate(2026, 11, 15);

        async Task<string[]> Names(string status) =>
            (await RowsAsync($"?period=2026-11&status={status}")).Select(r => r.GetProperty("student_name").GetString()!).Order().ToArray();

        Assert.Equal(new[] { "Paula Paga" }, await Names("paid"));
        Assert.Equal(new[] { "Ulises Debe", "Vera Anulada" }, await Names("overdue"));
        Assert.Equal(new[] { "Vera Anulada" }, await Names("voided"));
        Assert.Empty(await Names("pending"));
    }

    [Fact]
    public async Task Rows_inside_the_window_are_pending_and_carry_the_expected_amount()
    {
        await SeedOctoberAndNovemberAsync();

        var pending = await RowsAsync("?period=2026-11&status=pending");

        Assert.Equal(new[] { "Ulises Debe", "Vera Anulada" }, pending.Select(r => r.GetProperty("student_name").GetString()!).Order());
        Assert.All(pending, row =>
        {
            Assert.Equal("2026-11", row.GetProperty("period").GetString());
            Assert.Equal(15000m, row.GetProperty("amount").GetDecimal());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("payment_id").ValueKind);
        });
    }

    [Fact]
    public async Task Without_a_status_filter_every_state_is_listed_and_short_term_and_inactive_students_are_left_out()
    {
        await SeedOctoberAndNovemberAsync();
        Factory.Clock.SetBuenosAiresDate(2026, 11, 15);

        var rows = await RowsAsync("?period=2026-11");

        var byName = rows.GroupBy(r => r.GetProperty("student_name").GetString()!)
            .ToDictionary(g => g.Key, g => g.Select(r => r.GetProperty("status").GetString()!).Order().ToArray());
        Assert.Equal(new[] { "paid" }, byName["Paula Paga"]);
        Assert.Equal(new[] { "overdue" }, byName["Ulises Debe"]);
        Assert.Equal(new[] { "overdue", "voided" }, byName["Vera Anulada"]);
        Assert.False(byName.ContainsKey("Pablo Pase"));
        Assert.False(byName.ContainsKey("Ines Inactiva"));
    }

    [Fact]
    public async Task The_period_defaults_to_the_current_month_and_other_months_work()
    {
        await SeedOctoberAndNovemberAsync();

        var current = await RowsAsync();
        var october = await RowsAsync("?period=2026-10&status=overdue");

        Assert.Equal(current.Length, (await RowsAsync("?period=2026-11")).Length);
        Assert.Contains(october, r => r.GetProperty("student_name").GetString() == "Ulises Debe");
    }

    [Fact]
    public async Task Invalid_filters_are_rejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Owner.GetAsync("/api/gym/payments?status=gift")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Owner.GetAsync("/api/gym/payments?period=2026-13")).StatusCode);
    }

    // ---------- payment_status in the student list (T047) ----------

    private async Task<string?> StatusInListAsync(string name)
    {
        var list = (await ReadAsync(await Teacher.GetAsync("/api/gym/students"))).EnumerateArray();
        var status = list.Single(s => s.GetProperty("full_name").GetString() == name).GetProperty("payment_status");
        return status.ValueKind == JsonValueKind.Null ? null : status.GetString();
    }

    [Fact]
    public async Task Student_list_shows_paid_pending_overdue_for_the_current_month_and_null_without_monthly_membership()
    {
        await SeedOctoberAndNovemberAsync();

        Assert.Equal("paid", await StatusInListAsync("Paula Paga"));
        Assert.Equal("pending", await StatusInListAsync("Ulises Debe"));
        Assert.Equal("pending", await StatusInListAsync("Vera Anulada"));
        Assert.Null(await StatusInListAsync("Pablo Pase"));
        Assert.Null(await StatusInListAsync("Ines Inactiva"));

        Factory.Clock.SetBuenosAiresDate(2026, 11, 10);
        Assert.Equal("pending", await StatusInListAsync("Ulises Debe"));

        Factory.Clock.SetBuenosAiresDate(2026, 11, 11);
        Assert.Equal("overdue", await StatusInListAsync("Ulises Debe"));
        Assert.Equal("overdue", await StatusInListAsync("Vera Anulada"));
        Assert.Equal("paid", await StatusInListAsync("Paula Paga"));

        Factory.Clock.SetBuenosAiresDate(2026, 12, 1);
        Assert.Equal("pending", await StatusInListAsync("Paula Paga"));
        Assert.Equal("pending", await StatusInListAsync("Ulises Debe"));
    }

    [Fact]
    public async Task Student_detail_also_carries_payment_status_and_teachers_see_it_read_only()
    {
        var studentId = await MonthlyStudentAsync();

        var detail = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}"));
        Assert.Equal("pending", detail.GetProperty("payment_status").GetString());

        await PayMonthAsync(studentId);
        detail = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}"));
        Assert.Equal("paid", detail.GetProperty("payment_status").GetString());
    }

    [Fact]
    public async Task A_student_who_joins_the_monthly_membership_after_day_10_is_pending_that_month_never_overdue()
    {
        Factory.Clock.SetBuenosAiresDate(2026, 11, 15);
        var studentId = await MonthlyStudentAsync("late@example.com", "Lola Tarde");

        Assert.Equal("pending", await StatusInListAsync("Lola Tarde"));
        Factory.Clock.SetBuenosAiresDate(2026, 11, 30);
        Assert.Equal("pending", await StatusInListAsync("Lola Tarde"));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Teacher.PostAsJsonAsync($"/api/gym/students/{studentId}/payments", new { period = "2026-10" })).StatusCode);

        var november = await RowsAsync("?period=2026-11&status=pending");
        Assert.Contains(november, r => r.GetProperty("student_name").GetString() == "Lola Tarde");
        Assert.Empty(await RowsAsync("?period=2026-10"));
    }

    [Fact]
    public async Task Moving_from_passes_to_a_plan_starts_the_obligation_that_day()
    {
        var student = await CreateStudentAsync("Pedro Pase", "pedro@example.com");
        Assert.Null(await StatusInListAsync("Pedro Pase"));

        var plan = await CreatePlanAsync("Básico", 15000m);
        Factory.Clock.SetBuenosAiresDate(2026, 11, 20);
        await Teacher.PutAsJsonAsync($"/api/gym/students/{Id(student)}",
            new { full_name = "Pedro Pase", email = "pedro@example.com", plan_id = Id(plan) });

        Assert.Equal("pending", await StatusInListAsync("Pedro Pase"));
        Assert.Equal(HttpStatusCode.Created,
            (await Teacher.PostAsJsonAsync($"/api/gym/students/{Id(student)}/payments", new { period = "2026-11" })).StatusCode);
        Assert.Equal("paid", await StatusInListAsync("Pedro Pase"));
    }
}
