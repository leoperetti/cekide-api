using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public class AuthenticationTests : IDisposable
{
    private readonly CekideApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Request_without_token_is_unauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/api/gym/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_from_another_tenant_is_forbidden()
    {
        var client = ClientWithToken(TestTokens.Create("owner", tenant: "other-app"));

        var response = await client.GetAsync("/api/gym/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_without_tenant_claim_is_forbidden()
    {
        var client = ClientWithToken(TestTokens.Create("owner", tenant: null));

        var response = await client.GetAsync("/api/gym/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_for_the_gym_tenant_is_accepted()
    {
        var client = ClientWithToken(TestTokens.Create("teacher", subject: "sub-42"));

        var response = await client.GetAsync("/api/gym/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sub-42", body.GetProperty("sub").GetString());
        Assert.Equal("cekide-app", body.GetProperty("tenant").GetString());
        Assert.Contains("teacher", body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_unauthorized()
    {
        var client = ClientWithToken(TestTokens.Create("owner", signingKey: "a-completely-different-signing-key-0123456789"));

        var response = await client.GetAsync("/api/gym/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_with_wrong_issuer_is_unauthorized()
    {
        var client = ClientWithToken(TestTokens.Create("owner", issuer: "someone-else"));

        var response = await client.GetAsync("/api/gym/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_unauthorized()
    {
        var client = ClientWithToken(TestTokens.Create("owner", expires: DateTime.UtcNow.AddMinutes(-5)));

        var response = await client.GetAsync("/api/gym/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
