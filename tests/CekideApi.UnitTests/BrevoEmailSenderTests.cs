using System.Net;
using System.Text;
using System.Text.Json;
using CekideApi.Application.Abstractions;
using CekideApi.Infrastructure;
using CekideApi.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CekideApi.UnitTests;

public class BrevoEmailSenderTests
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

    private static readonly EmailMessage Message =
        new("ana@example.com", "Ana Pérez", "Cuota pendiente", "<p>Hola Ana</p>");

    private static BrevoOptions Options(string? apiKey = "test-api-key") =>
        new() { ApiKey = apiKey, FromEmail = "gym@example.com", FromName = "Cekide Gym" };

    private static BrevoEmailSender Sender(StubHandler handler, BrevoOptions? options = null) =>
        new(new HttpClient(handler), options ?? Options());

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Builds_the_expected_brevo_transactional_email_request()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{"messageId":"<1@brevo>"}"""));

        var result = await Sender(handler).SendAsync(Message);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.Request.RequestUri!.ToString());
        Assert.Equal("test-api-key", Assert.Single(handler.Request.Headers.GetValues("api-key")));
        Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);

        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal("gym@example.com", root.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("Cekide Gym", root.GetProperty("sender").GetProperty("name").GetString());
        var to = Assert.Single(root.GetProperty("to").EnumerateArray());
        Assert.Equal("ana@example.com", to.GetProperty("email").GetString());
        Assert.Equal("Ana Pérez", to.GetProperty("name").GetString());
        Assert.Equal("Cuota pendiente", root.GetProperty("subject").GetString());
        Assert.Equal("<p>Hola Ana</p>", root.GetProperty("htmlContent").GetString());
    }

    [Fact]
    public async Task A_brevo_error_response_is_translated_to_a_failed_result()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest, """{"code":"invalid_parameter","message":"Sender is invalid"}"""));

        var result = await Sender(handler).SendAsync(Message);

        Assert.False(result.Succeeded);
        Assert.Contains("400", result.Error);
        Assert.Contains("Sender is invalid", result.Error);
    }

    [Fact]
    public async Task A_network_failure_is_translated_to_a_failed_result()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var result = await Sender(handler).SendAsync(Message);

        Assert.False(result.Succeeded);
        Assert.Contains("connection refused", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_missing_api_key_fails_without_calling_brevo(string? apiKey)
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, "{}"));

        var result = await Sender(handler, Options(apiKey)).SendAsync(Message);

        Assert.False(result.Succeeded);
        Assert.Contains("Brevo", result.Error);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task A_missing_sender_address_fails_without_calling_brevo()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, "{}"));
        var options = new BrevoOptions { ApiKey = "key", FromEmail = null };

        var result = await Sender(handler, options).SendAsync(Message);

        Assert.False(result.Succeeded);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Long_error_bodies_are_truncated()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.InternalServerError, new string('x', 5000)));

        var result = await Sender(handler).SendAsync(Message);

        Assert.False(result.Succeeded);
        Assert.True(result.Error!.Length <= 500);
    }

    [Fact]
    public void The_sender_is_wired_with_the_brevo_settings_from_configuration()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Brevo:ApiKey"] = "from-config",
                ["Brevo:FromEmail"] = "gym@example.com"
            })
            .Build();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddLogging();
        services.AddInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<BrevoEmailSender>(provider.GetRequiredService<IEmailSender>());
        var options = provider.GetRequiredService<BrevoOptions>();
        Assert.Equal("from-config", options.ApiKey);
        Assert.Equal("gym@example.com", options.FromEmail);
        Assert.Equal("Cekide Gym", options.FromName);
    }}
