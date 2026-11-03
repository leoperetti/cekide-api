using System.Net;
using System.Net.Http.Json;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public class PlansTests : ApiTestBase
{
    [Fact]
    public async Task Owner_creates_a_plan()
    {
        var response = await Owner.PostAsJsonAsync("/api/gym/plans", new { name = " Musculación ", monthly_price = 15000.50m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var plan = await ReadAsync(response);
        Assert.True(plan.GetProperty("id").GetInt32() > 0);
        Assert.Equal("Musculación", plan.GetProperty("name").GetString());
        Assert.Equal(15000.50m, plan.GetProperty("monthly_price").GetDecimal());
        Assert.True(plan.GetProperty("is_active").GetBoolean());
    }

    [Fact]
    public async Task Teacher_cannot_create_a_plan()
    {
        var response = await Teacher.PostAsJsonAsync("/api/gym/plans", new { name = "Plan", monthly_price = 100m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_edit_a_plan_or_change_prices()
    {
        var plan = await CreatePlanAsync();

        var response = await Teacher.PutAsJsonAsync(
            $"/api/gym/plans/{plan.GetProperty("id").GetInt32()}", new { name = "Plan", monthly_price = 1m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_change_pass_tariffs()
    {
        var response = await Teacher.PutAsJsonAsync("/api/gym/pass-tariffs", new[] { new { type = "day", price = 100m } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Students_and_anonymous_users_cannot_use_plans()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync("/api/gym/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync("/api/gym/pass-tariffs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync("/api/gym/plans")).StatusCode);
    }

    [Fact]
    public async Task Owner_and_teacher_can_list_plans()
    {
        await CreatePlanAsync("Básico", 100m);
        await CreatePlanAsync("Premium", 200m);

        foreach (var client in new[] { Owner, Teacher })
        {
            var response = await client.GetAsync("/api/gym/plans");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var plans = await ReadAsync(response);
            Assert.Equal(new[] { "Básico", "Premium" }, plans.EnumerateArray().Select(p => p.GetProperty("name").GetString()).Order());
        }
    }

    [Fact]
    public async Task Owner_edits_a_plan_and_changes_its_price()
    {
        var plan = await CreatePlanAsync();

        var response = await Owner.PutAsJsonAsync(
            $"/api/gym/plans/{plan.GetProperty("id").GetInt32()}", new { name = "Básico Plus", monthly_price = 18000m, is_active = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadAsync(response);
        Assert.Equal("Básico Plus", updated.GetProperty("name").GetString());
        Assert.Equal(18000m, updated.GetProperty("monthly_price").GetDecimal());
        Assert.False(updated.GetProperty("is_active").GetBoolean());
    }

    [Fact]
    public async Task Duplicate_plan_names_are_a_conflict_regardless_of_case()
    {
        await CreatePlanAsync("Básico");

        var response = await Owner.PostAsJsonAsync("/api/gym/plans", new { name = "BÁSICO", monthly_price = 1m });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Renaming_a_plan_to_an_existing_name_is_a_conflict()
    {
        await CreatePlanAsync("Básico");
        var other = await CreatePlanAsync("Premium");

        var response = await Owner.PutAsJsonAsync(
            $"/api/gym/plans/{other.GetProperty("id").GetInt32()}", new { name = "Básico", monthly_price = 1m });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("", 100)]
    [InlineData("Plan", 0)]
    [InlineData("Plan", -10)]
    public async Task Invalid_plans_are_rejected(string name, decimal price)
    {
        var response = await Owner.PostAsJsonAsync("/api/gym/plans", new { name, monthly_price = price });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_price_is_rejected()
    {
        var response = await Owner.PostAsJsonAsync("/api/gym/plans", new { name = "Plan" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_an_unknown_plan_is_not_found()
    {
        var response = await Owner.PutAsJsonAsync("/api/gym/plans/999", new { name = "Plan", monthly_price = 1m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Owner_sets_and_everyone_on_staff_reads_pass_tariffs()
    {
        var put = await Owner.PutAsJsonAsync("/api/gym/pass-tariffs", new[]
        {
            new { type = "day", price = 3000m },
            new { type = "week", price = 12000m },
            new { type = "fifteen_days", price = 20000m }
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var update = await Owner.PutAsJsonAsync("/api/gym/pass-tariffs", new[] { new { type = "day", price = 3500m } });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var get = await Teacher.GetAsync("/api/gym/pass-tariffs");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var tariffs = (await ReadAsync(get)).EnumerateArray().ToDictionary(
            t => t.GetProperty("type").GetString()!, t => t.GetProperty("price").GetDecimal());
        Assert.Equal(3, tariffs.Count);
        Assert.Equal(3500m, tariffs["day"]);
        Assert.Equal(12000m, tariffs["week"]);
        Assert.Equal(20000m, tariffs["fifteen_days"]);
    }

    [Fact]
    public async Task Invalid_pass_tariffs_are_rejected()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Owner.PutAsJsonAsync("/api/gym/pass-tariffs", new[] { new { type = "month", price = 100m } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Owner.PutAsJsonAsync("/api/gym/pass-tariffs", new[] { new { type = "day", price = 0m } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Owner.PutAsJsonAsync("/api/gym/pass-tariffs", new[]
            {
                new { type = "day", price = 100m },
                new { type = "day", price = 200m }
            })).StatusCode);
    }
}
