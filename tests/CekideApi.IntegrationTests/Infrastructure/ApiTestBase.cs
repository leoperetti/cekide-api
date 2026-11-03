using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CekideApi.IntegrationTests.Infrastructure;

/// <summary>Per-test host (fresh database) with ready-made clients for each role.</summary>
public abstract class ApiTestBase : IDisposable
{
    protected ApiTestBase()
    {
        Factory = new CekideApiFactory();
        Owner = Factory.CreateClientFor("owner", "owner-1");
        Teacher = Factory.CreateClientFor("teacher", "teacher-1");
        Student = Factory.CreateClientFor("user", "student-1");
        Anonymous = Factory.CreateClient();
    }

    protected CekideApiFactory Factory { get; }

    protected HttpClient Owner { get; }

    protected HttpClient Teacher { get; }

    protected HttpClient Student { get; }

    protected HttpClient Anonymous { get; }

    public void Dispose()
    {
        Owner.Dispose();
        Teacher.Dispose();
        Student.Dispose();
        Anonymous.Dispose();
        Factory.Dispose();
    }

    protected static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    protected async Task<JsonElement> CreatePlanAsync(string name = "Básico", decimal price = 15000m)
    {
        var response = await Owner.PostAsJsonAsync("/api/gym/plans", new { name, monthly_price = price });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    protected async Task<JsonElement> CreateStudentAsync(
        string fullName = "Ana Pérez", string email = "ana@example.com", int? planId = null, HttpClient? client = null)
    {
        var response = await (client ?? Teacher).PostAsJsonAsync(
            "/api/gym/students", new { full_name = fullName, email, phone = "1155550000", plan_id = planId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }
}
