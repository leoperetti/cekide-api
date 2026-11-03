using System.Net;
using System.Text;
using System.Text.Json;
using CekideApi.Domain;
using CekideApi.Infrastructure.Invitations;

namespace CekideApi.UnitTests;

public class AuthcraftInvitationClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static AuthcraftInvitationClient Client(StubHandler handler, string? baseUrl = "https://auth.example.com/") =>
        new(new HttpClient(handler), new AuthcraftOptions { BaseUrl = baseUrl });

    private const string Created = """{"invitationId":42,"expiresAt":"2026-11-12T15:00:00+00:00"}""";

    [Fact]
    public async Task Create_posts_the_email_to_authcraft_with_the_callers_bearer_token()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, Created));

        var result = await Client(handler).CreateAsync("ana@example.com", "caller-token");

        Assert.Equal(42, result.InvitationId);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero), result.ExpiresAt);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://auth.example.com/api/invitations", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("caller-token", handler.Request.Headers.Authorization.Parameter);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("ana@example.com", body.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Resend_posts_to_the_invitation_resend_endpoint()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, Created));

        var result = await Client(handler).ResendAsync(41, "caller-token");

        Assert.Equal(42, result.InvitationId);
        Assert.Equal("https://auth.example.com/api/invitations/41/resend", handler.Request!.RequestUri!.ToString());
        Assert.Equal("caller-token", handler.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task The_base_url_may_omit_the_trailing_slash()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, Created));

        await Client(handler, "https://auth.example.com").CreateAsync("ana@example.com", "t");

        Assert.Equal("https://auth.example.com/api/invitations", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_conflict_is_propagated_with_the_authcraft_detail()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict, """{"title":"Invitación pendiente","detail":"Ya hay una invitación pendiente."}"""));

        var error = await Assert.ThrowsAsync<ConflictException>(() => Client(handler).CreateAsync("ana@example.com", "t"));

        Assert.Equal("Ya hay una invitación pendiente.", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, typeof(DomainValidationException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(ForbiddenException))]
    [InlineData(HttpStatusCode.Unauthorized, typeof(ForbiddenException))]
    [InlineData(HttpStatusCode.NotFound, typeof(NotFoundException))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(UpstreamServiceException))]
    [InlineData(HttpStatusCode.BadGateway, typeof(UpstreamServiceException))]
    public async Task Authcraft_errors_map_to_domain_exceptions(HttpStatusCode status, Type expected)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        var error = await Assert.ThrowsAnyAsync<DomainException>(() => Client(handler).ResendAsync(41, "t"));

        Assert.IsType(expected, error);
    }

    [Fact]
    public async Task A_network_failure_is_an_upstream_error()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<UpstreamServiceException>(() => Client(handler).CreateAsync("ana@example.com", "t"));
    }

    [Fact]
    public async Task An_unreadable_success_response_is_an_upstream_error()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, "{}"));

        await Assert.ThrowsAsync<UpstreamServiceException>(() => Client(handler).CreateAsync("ana@example.com", "t"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    public async Task A_missing_base_url_is_an_upstream_error_without_calling_authcraft(string? baseUrl)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, Created));

        await Assert.ThrowsAsync<UpstreamServiceException>(() => Client(handler, baseUrl).CreateAsync("ana@example.com", "t"));
        Assert.Null(handler.Request);
    }
}
