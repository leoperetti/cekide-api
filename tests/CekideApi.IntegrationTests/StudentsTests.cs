using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public class StudentsTests : ApiTestBase
{
    private static int Id(JsonElement element) => element.GetProperty("id").GetInt32();

    [Fact]
    public async Task Duplicate_email_does_not_report_a_dni_conflict_when_dni_is_available()
    {
        await CreateStudentAsync(email: "index_ix_students_dni@example.test");
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Otro Alumno", email = "index_ix_students_dni@example.test", dni = "01234567"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False((await ReadAsync(response)).TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Structured_profile_is_normalized_and_duplicate_dni_is_a_conflict()
    {
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana María de la Cruz",
            first_name = " Ana María ",
            last_name = " de la Cruz ",
            email = "profile@example.test",
            dni = "01.234.567"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var profile = await ReadAsync(response);
        Assert.Equal("Ana María", profile.GetProperty("first_name").GetString());
        Assert.Equal("de la Cruz", profile.GetProperty("last_name").GetString());
        Assert.Equal("01234567", profile.GetProperty("dni").GetString());

        response = await Owner.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Otro Alumno",
            email = "other@example.test",
            dni = "01234567"
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("dni_already_exists", (await ReadAsync(response)).GetProperty("code").GetString());
        var found = await ReadAsync(await Teacher.GetAsync("/api/gym/students?search=de%20la%20Cruz"));
        Assert.Single(found.EnumerateArray());
    }

    [Fact]
    public async Task Legacy_profile_updates_preserve_dni_and_invalidate_only_changed_names()
    {
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            first_name = "Ana",
            last_name = "Pérez",
            email = "profile@example.test",
            dni = "1234567"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = Id(await ReadAsync(response));
        response = await Teacher.PutAsJsonAsync($"/api/gym/students/{id}", new
        {
            full_name = "Ana Pérez",
            email = "profile@example.test",
            dni = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await ReadAsync(response);
        Assert.Equal("Ana", profile.GetProperty("first_name").GetString());
        Assert.Equal("1234567", profile.GetProperty("dni").GetString());
        response = await Teacher.PutAsJsonAsync($"/api/gym/students/{id}", new
        {
            full_name = "Ana Gómez",
            email = "profile@example.test"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        profile = await ReadAsync(response);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("first_name").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("last_name").ValueKind);
        Assert.Equal("1234567", profile.GetProperty("dni").GetString());
    }

    [Theory]
    [InlineData("", "Ana", "Pérez", "Ana Pérez")]
    [InlineData("12-345-678", "Ana", "Pérez", "Ana Pérez")]
    [InlineData("12345678", "Ana", null, "Ana Pérez")]
    [InlineData("12345678", "Ana", "Pérez", "Contradictorio")]
    public async Task Invalid_profile_is_rejected_without_writes(
        string dni, string firstName, string? lastName, string fullName)
    {
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = fullName,
            first_name = firstName,
            last_name = lastName,
            email = "invalid@example.test",
            dni
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await ReadAsync(await Teacher.GetAsync("/api/gym/students"))).EnumerateArray());
    }

    [Fact]
    public async Task Teacher_creates_a_student_with_a_plan()
    {
        var plan = await CreatePlanAsync("Básico", 15000m);

        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "  Ana Pérez ",
            email = " Ana@Example.com ",
            phone = "1155550000",
            plan_id = Id(plan)
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var student = await ReadAsync(response);
        Assert.Equal("Ana Pérez", student.GetProperty("full_name").GetString());
        Assert.Equal("ana@example.com", student.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, student.GetProperty("first_name").ValueKind);
        Assert.Equal(JsonValueKind.Null, student.GetProperty("last_name").ValueKind);
        Assert.Equal(JsonValueKind.Null, student.GetProperty("dni").ValueKind);
        Assert.Equal(Id(plan), student.GetProperty("plan_id").GetInt32());
        Assert.Equal("Básico", student.GetProperty("plan_name").GetString());
        Assert.True(student.GetProperty("is_active").GetBoolean());
    }

    [Fact]
    public async Task Owner_can_create_a_student_without_a_plan()
    {
        var student = await CreateStudentAsync(client: Owner);

        Assert.Equal(JsonValueKind.Null, student.GetProperty("plan_id").ValueKind);
    }

    [Fact]
    public async Task Students_get_and_detail_work_for_staff()
    {
        var created = await CreateStudentAsync();

        var response = await Owner.GetAsync($"/api/gym/students/{Id(created)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ana@example.com", (await ReadAsync(response)).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Teacher.GetAsync("/api/gym/students/999")).StatusCode);
    }

    [Fact]
    public async Task List_supports_search_by_name_or_email_and_the_active_filter()
    {
        await CreateStudentAsync("Ana Pérez", "ana@example.com");
        await CreateStudentAsync("Bruno Díaz", "bruno@gym.com");
        var carla = await CreateStudentAsync("Carla Gómez", "carla@example.com");
        await Teacher.PostAsync($"/api/gym/students/{Id(carla)}/deactivate", null);

        async Task<string[]> Names(string query)
        {
            var response = await Teacher.GetAsync($"/api/gym/students{query}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await ReadAsync(response)).EnumerateArray()
                .Select(s => s.GetProperty("full_name").GetString()!).Order().ToArray();
        }

        Assert.Equal(new[] { "Ana Pérez", "Bruno Díaz", "Carla Gómez" }, await Names(""));
        Assert.Equal(new[] { "Ana Pérez" }, await Names("?search=pérez"));
        Assert.Equal(new[] { "Bruno Díaz" }, await Names("?search=GYM.com"));
        Assert.Equal(new[] { "Ana Pérez", "Bruno Díaz" }, await Names("?active=true"));
        Assert.Equal(new[] { "Carla Gómez" }, await Names("?active=false"));
        Assert.Equal(new[] { "Carla Gómez" }, await Names("?search=carla&active=false"));
        Assert.Empty(await Names("?search=carla&active=true"));
    }

    [Fact]
    public async Task List_includes_payment_status_that_is_null_until_payments_exist()
    {
        await CreateStudentAsync();

        var list = await ReadAsync(await Teacher.GetAsync("/api/gym/students"));

        var student = list.EnumerateArray().Single();
        Assert.True(student.TryGetProperty("payment_status", out var status));
        Assert.Equal(JsonValueKind.Null, status.ValueKind);
    }

    [Fact]
    public async Task Teacher_edits_a_student_and_assigns_a_plan()
    {
        var plan = await CreatePlanAsync();
        var student = await CreateStudentAsync();

        var response = await Teacher.PutAsJsonAsync($"/api/gym/students/{Id(student)}", new
        {
            full_name = "Ana María Pérez",
            email = "ana.maria@example.com",
            phone = (string?)null,
            plan_id = Id(plan)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadAsync(response);
        Assert.Equal("Ana María Pérez", updated.GetProperty("full_name").GetString());
        Assert.Equal("ana.maria@example.com", updated.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("phone").ValueKind);
        Assert.Equal(Id(plan), updated.GetProperty("plan_id").GetInt32());
    }

    [Fact]
    public async Task Changing_plan_applies_from_the_next_month_and_keeps_the_current_one_meanwhile()
    {
        var basic = await CreatePlanAsync("Básico", 100m);
        var premium = await CreatePlanAsync("Premium", 200m);
        var student = await CreateStudentAsync(planId: Id(basic));

        var response = await Teacher.PutAsJsonAsync($"/api/gym/students/{Id(student)}", new
        {
            full_name = "Ana Pérez",
            email = "ana@example.com",
            plan_id = Id(premium)
        });

        var updated = await ReadAsync(response);
        Assert.Equal(Id(basic), updated.GetProperty("plan_id").GetInt32());
        Assert.Equal(Id(premium), updated.GetProperty("pending_plan_id").GetInt32());
        Assert.Equal("2026-12-01", updated.GetProperty("pending_plan_effective_from").GetString());
    }

    [Fact]
    public async Task Editing_an_unknown_student_is_not_found()
    {
        var response = await Teacher.PutAsJsonAsync("/api/gym/students/999", new { full_name = "X", email = "x@example.com" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deactivating_keeps_the_student_in_the_list()
    {
        var student = await CreateStudentAsync();

        var response = await Teacher.PostAsync($"/api/gym/students/{Id(student)}/deactivate", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{Id(student)}"));
        Assert.False(detail.GetProperty("is_active").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await Teacher.PostAsync("/api/gym/students/999/deactivate", null)).StatusCode);
    }

    public static IEnumerable<object?[]> InvalidEmails => new[]
    {
        new object?[] { null },
        new object?[] { "" },
        new object?[] { "   " },
        new object?[] { "not-an-email" },
        new object?[] { "a@b" }
    };

    [Theory]
    [MemberData(nameof(InvalidEmails))]
    public async Task Missing_null_empty_or_invalid_email_is_rejected_on_create(string? email)
    {
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new { full_name = "Ana", email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Email_property_omitted_is_rejected_on_create()
    {
        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new { full_name = "Ana" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidEmails))]
    public async Task Missing_null_empty_or_invalid_email_is_rejected_on_update(string? email)
    {
        var student = await CreateStudentAsync();

        var response = await Teacher.PutAsJsonAsync($"/api/gym/students/{Id(student)}", new { full_name = "Ana", email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_is_a_conflict_even_with_different_case_or_spaces()
    {
        await CreateStudentAsync(email: "ana@example.com");

        var response = await Teacher.PostAsJsonAsync("/api/gym/students", new { full_name = "Otra", email = " ANA@example.com " });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Updating_to_an_email_used_by_another_student_is_a_conflict_but_keeping_your_own_is_fine()
    {
        await CreateStudentAsync("Ana", "ana@example.com");
        var bruno = await CreateStudentAsync("Bruno", "bruno@example.com");

        var conflict = await Teacher.PutAsJsonAsync($"/api/gym/students/{Id(bruno)}", new { full_name = "Bruno", email = "ana@example.com" });
        var same = await Teacher.PutAsJsonAsync($"/api/gym/students/{Id(bruno)}", new { full_name = "Bruno B", email = "bruno@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task Unknown_or_inactive_plan_is_rejected()
    {
        var plan = await CreatePlanAsync();
        await Owner.PutAsJsonAsync($"/api/gym/plans/{Id(plan)}", new { name = "Básico", monthly_price = 100m, is_active = false });

        var unknown = await Teacher.PostAsJsonAsync("/api/gym/students", new { full_name = "Ana", email = "a@example.com", plan_id = 999 });
        var inactive = await Teacher.PostAsJsonAsync("/api/gym/students", new { full_name = "Ana", email = "a@example.com", plan_id = Id(plan) });

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
    }

    [Fact]
    public async Task Students_role_cannot_use_staff_student_endpoints()
    {
        var student = await CreateStudentAsync();
        var id = Id(student);

        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync("/api/gym/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.GetAsync($"/api/gym/students/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Student.PostAsJsonAsync("/api/gym/students", new { full_name = "X", email = "x@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Student.PutAsJsonAsync($"/api/gym/students/{id}", new { full_name = "X", email = "x@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Student.PostAsync($"/api/gym/students/{id}/deactivate", null)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_unauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.GetAsync("/api/gym/students")).StatusCode);
    }
}
