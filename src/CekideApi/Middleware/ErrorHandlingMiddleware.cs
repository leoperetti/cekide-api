using CekideApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Middleware;

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var (status, title, detail) = Map(exception);

            if (status >= 500)
            {
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            else
            {
                logger.LogInformation("Request rejected with {Status}: {Message}", status, exception.Message);
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails { Status = status, Title = title, Detail = detail },
                options: null,
                contentType: "application/problem+json");
        }
    }

    private static (int Status, string Title, string Detail) Map(Exception exception) => exception switch
    {
        DomainValidationException => (StatusCodes.Status400BadRequest, "Bad Request", exception.Message),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", exception.Message),
        NotFoundException => (StatusCodes.Status404NotFound, "Not Found", exception.Message),
        ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),
        PayloadTooLargeException => (StatusCodes.Status413PayloadTooLarge, "Payload Too Large", exception.Message),
        UpstreamServiceException => (StatusCodes.Status502BadGateway, "Bad Gateway", exception.Message),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.")
    };
}
