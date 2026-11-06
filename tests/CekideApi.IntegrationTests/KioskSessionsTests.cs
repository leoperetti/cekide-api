using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public sealed class KioskSessionsTests : ApiTestBase
{
    [Fact]
    public async Task Session_can_be_activated_once_and_revoked()
    {
        var activation = await CreateKioskAsync();
        var sessionId = activation.GetProperty("session").GetProperty("id").GetInt32();
        var activationCode = activation.GetProperty("activation_code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(activationCode));

        var response = await Anonymous.PostAsJsonAsync(
            "/api/gym/kiosk/activate", new { activation_code = activationCode });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var credential = await ReadAsync(response);
        var token = credential.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        response = await Anonymous.PostAsJsonAsync(
            "/api/gym/kiosk/activate", new { activation_code = activationCode });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_activation_code", (await ReadAsync(response)).GetProperty("code").GetString());

        var planResponse = await Owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = "Attendance",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        Assert.Equal(HttpStatusCode.Created, planResponse.StatusCode);
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();
        response = await Teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Ana Pérez",
            email = "kiosk-student@example.com",
            dni = "12345678",
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var kiosk = KioskClient(token!);
        response = await kiosk.PostAsJsonAsync(
            "/api/gym/kiosk/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var checkIn = await ReadAsync(response);
        Assert.Equal("kiosk", checkIn.GetProperty("attendance").GetProperty("source").GetString());
        Assert.False(checkIn.TryGetProperty("student_id", out _));
        Assert.False(checkIn.GetProperty("attendance").TryGetProperty("dni", out _));

        response = await Owner.PostAsync($"/api/gym/kiosk-sessions/{sessionId}/revoke", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        response = await kiosk.PostAsJsonAsync("/api/gym/kiosk/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var sessions = await ReadAsync(await Owner.GetAsync("/api/gym/kiosk-sessions"));
        Assert.Equal("revoked", sessions[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Expired_activation_and_credential_are_rejected()
    {
        var activation = await CreateKioskAsync();
        var activationCode = activation.GetProperty("activation_code").GetString()!;
        Factory.Clock.Set(Factory.Clock.UtcNow.AddMinutes(11));

        var response = await Anonymous.PostAsJsonAsync(
            "/api/gym/kiosk/activate", new { activation_code = activationCode });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        activation = await CreateKioskAsync();
        activationCode = activation.GetProperty("activation_code").GetString()!;
        var credentialResponse = await Anonymous.PostAsJsonAsync(
            "/api/gym/kiosk/activate", new { activation_code = activationCode });
        Assert.Equal(HttpStatusCode.OK, credentialResponse.StatusCode);
        var credential = await ReadAsync(credentialResponse);
        Factory.Clock.Set(Factory.Clock.UtcNow.AddHours(13));

        using var kiosk = KioskClient(credential.GetProperty("token").GetString()!);
        response = await kiosk.PostAsJsonAsync(
            "/api/gym/kiosk/attendance/check-in", new { dni = "12345678" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Activation_is_limited_to_five_attempts_per_minute_per_ip()
    {
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            last?.Dispose();
            last = await Anonymous.PostAsJsonAsync(
                "/api/gym/kiosk/activate", new { activation_code = "invalid-code" });
            Assert.Equal(attempt < 5 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests, last.StatusCode);
        }

        Assert.Equal("no-store", last!.Headers.CacheControl?.ToString());
        Assert.True(last.Headers.Contains("Retry-After"));
        Assert.Equal("rate_limited", (await ReadAsync(last)).GetProperty("code").GetString());
        last.Dispose();
    }

    [Fact]
    public async Task Check_in_limit_is_sixty_requests_per_kiosk_session()
    {
        var firstCredential = await ActivateKioskAsync();
        using var firstKiosk = KioskClient(firstCredential);

        for (var attempt = 0; attempt < 61; attempt++)
        {
            using var response = await firstKiosk.PostAsJsonAsync(
                "/api/gym/kiosk/attendance/check-in", new { dni = "invalid" });
            Assert.Equal(
                attempt < 60 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }

        var secondCredential = await ActivateKioskAsync();
        using var secondKiosk = KioskClient(secondCredential);
        using var secondResponse = await secondKiosk.PostAsJsonAsync(
            "/api/gym/kiosk/attendance/check-in", new { dni = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
    }

    private async Task<JsonElement> CreateKioskAsync()
    {
        var response = await Owner.PostAsJsonAsync("/api/gym/kiosk-sessions", new { label = "Front desk" });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await ReadAsync(response);
    }

    private async Task<string> ActivateKioskAsync()
    {
        var activation = await CreateKioskAsync();
        var response = await Anonymous.PostAsJsonAsync(
            "/api/gym/kiosk/activate",
            new { activation_code = activation.GetProperty("activation_code").GetString() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("token").GetString()!;
    }

    private HttpClient KioskClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Kiosk", token);
        return client;
    }
}
