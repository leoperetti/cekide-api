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
    public async Task Resend_posts_the_latest_email_and_student_profile()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, Created));
        var profile = new StudentInvitationProfile(73, "Ana", "Actualizada", "Ana Actualizada", "+541166660000", null);

        await Client(handler).ResendAsync(41, "ana.new@example.com", profile, "caller-token");

        Assert.Equal("https://auth.example.com/api/invitations/41/resend", handler.Request!.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("ana.new@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal(73, body.RootElement.GetProperty("studentProfile").GetProperty("studentId").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("studentProfile").GetProperty("dni").ValueKind);
    }

    [Fact]
    public async Task Accepted_profile_is_fetched_with_the_callers_bearer_token()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            {
              "invitationId": 41,
              "studentId": 73,
              "identityId": "identity-17",
              "acceptedAt": "2026-11-05T15:00:00Z",
              "prefill": { "firstName": "Ana", "lastName": "Pérez", "fullName": "Ana Pérez", "phoneNumber": null, "dni": null },
              "registered": { "firstName": "Ana", "lastName": "Pérez", "phoneNumber": "+541155550000", "dni": "01234567" }
            }
            """));

        var result = await Client(handler).GetAcceptedProfileAsync(41, "caller-token");

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://auth.example.com/api/invitations/41/accepted-profile", handler.Request.RequestUri!.ToString());
        Assert.Equal("caller-token", handler.Request.Headers.Authorization!.Parameter);
        Assert.NotNull(result);
        Assert.Equal(73, result.StudentId);
        Assert.Equal("identity-17", result.IdentityId);
        Assert.Equal("01234567", result.Registered.Dni);
    }

    [Fact]
    public async Task Create_posts_a_typed_student_profile()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, Created));
        var profile = new StudentInvitationProfile(73, "Ana María", "de la Cruz", "Ana María de la Cruz", "+541155550000", "01234567");

        await Client(handler).CreateAsync("ana@example.com", profile, "caller-token");

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("ana@example.com", body.RootElement.GetProperty("email").GetString());
        var studentProfile = body.RootElement.GetProperty("studentProfile");
        Assert.Equal(73, studentProfile.GetProperty("studentId").GetInt32());
        Assert.Equal("Ana María", studentProfile.GetProperty("firstName").GetString());
        Assert.Equal("01234567", studentProfile.GetProperty("dni").GetString());
        Assert.Equal("caller-token", handler.Request!.Headers.Authorization!.Parameter);
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
