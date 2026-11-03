using System.Net;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public class CorsTests : IDisposable
{
    private readonly CekideApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/gym/me");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return request;
    }

    private static string? AllowedOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;

    [Fact]
    public async Task Origin_of_the_tenant_is_allowed()
    {
        var response = await _factory.CreateClient().SendAsync(Preflight(CekideApiFactory.TenantOrigin));

        Assert.Equal(CekideApiFactory.TenantOrigin, AllowedOrigin(response));
    }

    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("http://127.0.0.1:4200")]
    public async Task Localhost_origins_are_allowed(string origin)
    {
        var response = await _factory.CreateClient().SendAsync(Preflight(origin));

        Assert.Equal(origin, AllowedOrigin(response));
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("https://cekide-gym.example.com.evil.net")]
    [InlineData("https://localhost.evil.net")]
    public async Task Origins_outside_the_allow_list_are_rejected(string origin)
    {
        var response = await _factory.CreateClient().SendAsync(Preflight(origin));

        Assert.Null(AllowedOrigin(response));
    }

    [Fact]
    public async Task Actual_requests_carry_cors_headers_only_for_allowed_origins()
    {
        var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/gym/me");
        allowed.Headers.Add("Origin", CekideApiFactory.TenantOrigin);
        var rejected = new HttpRequestMessage(HttpMethod.Get, "/api/gym/me");
        rejected.Headers.Add("Origin", "https://evil.example.com");
        var client = _factory.CreateClient();

        var allowedResponse = await client.SendAsync(allowed);
        var rejectedResponse = await client.SendAsync(rejected);

        Assert.Equal(HttpStatusCode.Unauthorized, allowedResponse.StatusCode);
        Assert.Equal(CekideApiFactory.TenantOrigin, AllowedOrigin(allowedResponse));
        Assert.Null(AllowedOrigin(rejectedResponse));
    }
}
