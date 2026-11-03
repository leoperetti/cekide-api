using System.Net;
using CekideApi.IntegrationTests.Infrastructure;

namespace CekideApi.IntegrationTests;

public class PolicyTests : IDisposable
{
    private readonly CekideApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("owner", "owner", 200)]
    [InlineData("teacher", "owner", 403)]
    [InlineData("user", "owner", 403)]
    [InlineData("owner", "staff", 200)]
    [InlineData("teacher", "staff", 200)]
    [InlineData("user", "staff", 403)]
    [InlineData("owner", "student", 403)]
    [InlineData("teacher", "student", 403)]
    [InlineData("user", "student", 200)]
    public async Task Policies_allow_only_the_expected_roles(string role, string policy, int expectedStatus)
    {
        var client = _factory.CreateClientFor(role);

        var response = await client.GetAsync($"/test/policies/{policy}");

        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("staff")]
    [InlineData("student")]
    public async Task Policies_require_authentication(string policy)
    {
        var response = await _factory.CreateClient().GetAsync($"/test/policies/{policy}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("owner", "owner")]
    [InlineData("teacher", "staff")]
    [InlineData("user", "student")]
    public async Task Policies_reject_the_right_role_from_another_tenant(string role, string policy)
    {
        var client = _factory.CreateClientFor(role, tenant: "other-app");

        var response = await client.GetAsync($"/test/policies/{policy}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_roles_are_rejected_everywhere()
    {
        var client = _factory.CreateClientFor("superadmin");

        foreach (var policy in new[] { "owner", "staff", "student" })
        {
            var response = await client.GetAsync($"/test/policies/{policy}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
