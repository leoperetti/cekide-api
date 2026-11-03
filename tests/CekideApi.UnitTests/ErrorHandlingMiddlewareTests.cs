using System.Text.Json;
using CekideApi.Domain;
using CekideApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

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
}
