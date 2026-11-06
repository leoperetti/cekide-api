using System.Text.Json;
using CekideApi.Domain;
using CekideApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace CekideApi.UnitTests;

public class ErrorHandlingMiddlewareTests
{
    private static async Task<(DefaultHttpContext Context, JsonElement Body)> InvokeAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ErrorHandlingMiddleware(_ => throw exception, NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, body.RootElement.Clone());
    }

    [Theory]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(ForbiddenException), 403)]
    [InlineData(typeof(ConflictException), 409)]
    [InlineData(typeof(DomainValidationException), 400)]
    [InlineData(typeof(PayloadTooLargeException), 413)]
    [InlineData(typeof(UpstreamServiceException), 502)]
    public async Task Domain_exceptions_are_mapped_to_problem_details(Type exceptionType, int expectedStatus)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "visible message")!;

        var (context, body) = await InvokeAsync(exception);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        Assert.Equal(expectedStatus, body.GetProperty("status").GetInt32());
        Assert.Equal("visible message", body.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
        Assert.False(body.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Unhandled_exceptions_return_500_without_leaking_details()
    {
        var (context, body) = await InvokeAsync(new InvalidOperationException("connection string Server=prod;Password=hunter2"));

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.DoesNotContain("hunter2", body.GetRawText());
        Assert.DoesNotContain("InvalidOperationException", body.GetRawText());
    }

    [Fact]
    public async Task Requests_without_errors_pass_through_untouched()
    {
        var context = new DefaultHttpContext();
        var called = false;
        var middleware = new ErrorHandlingMiddleware(_ => { called = true; return Task.CompletedTask; }, NullLogger<ErrorHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task Attendance_errors_add_code_without_changing_problem_details()
    {
        var (context, body) = await InvokeAsync(
            new AttendanceException(409, "attendance_limits_missing", "Configure plan limits."));

        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("Conflict", body.GetProperty("title").GetString());
        Assert.Equal("Configure plan limits.", body.GetProperty("detail").GetString());
        Assert.Equal("attendance_limits_missing", body.GetProperty("code").GetString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Logs_do_not_include_exception_payloads_or_request_secrets()
    {
        var logger = new RecordingLogger();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/gym/me/check-ins";
        context.Request.Headers.Authorization = "Bearer synthetic-test-token";
        context.Response.Body = new MemoryStream();
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new InvalidOperationException("DNI 00123456; synthetic-test-token"), logger);

        await middleware.InvokeAsync(context);

        var logged = string.Join(" ", logger.Messages);
        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain("00123456", logged);
        Assert.DoesNotContain("synthetic-test-token", logged);
    }

    [Fact]
    public async Task Attendance_server_errors_keep_internal_details_private()
    {
        var (context, body) = await InvokeAsync(
            new AttendanceException(503, "attendance_unavailable", "DNI 00123456; synthetic-test-token"));

        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("attendance_unavailable", body.GetProperty("code").GetString());
        Assert.DoesNotContain("00123456", body.GetRawText());
        Assert.DoesNotContain("synthetic-test-token", body.GetRawText());
    }

    [Theory]
    [InlineData(200, "invalid_dni")]
    [InlineData(600, "invalid_dni")]
    [InlineData(400, "Invalid DNI")]
    public void Invalid_error_metadata_is_rejected(int status, string code)
    {
        Assert.ThrowsAny<ArgumentException>(() => new AttendanceException(status, code, "Test error."));
    }

    private sealed class RecordingLogger : ILogger<ErrorHandlingMiddleware>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                Messages.Add(exception.ToString());
            }
        }
    }
}
