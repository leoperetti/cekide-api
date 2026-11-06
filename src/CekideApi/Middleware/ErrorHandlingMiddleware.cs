using CekideApi.Domain;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Middleware;

public sealed class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/gym/me") ||
            context.Request.Path.StartsWithSegments("/api/gym/attendance-notifications") ||
            context.Request.Path.StartsWithSegments("/api/gym/students") &&
            context.Request.Path.Value?.Contains("/recovery-reservations", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var (status, title, detail) = Map(exception);

            if (status >= 500)
            {
                logger.LogError("Request failed with {Status}, exception {ExceptionType}, trace {TraceId}",
                    status, exception.GetType().Name, context.TraceIdentifier);
            }
            else
            {
                logger.LogInformation("Request rejected with {Status}, exception {ExceptionType}, trace {TraceId}",
                    status, exception.GetType().Name, context.TraceIdentifier);
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
            if (exception is AttendanceException attendance)
            {
                problem.Extensions["code"] = attendance.Code;
                context.Response.Headers.CacheControl = "no-store";
            }

            if (context.Request.Path.StartsWithSegments("/api/gym/shifts") ||
                context.Request.Path.StartsWithSegments("/api/gym/attendance-notifications") ||
                context.Request.Path.StartsWithSegments("/api/gym/me") ||
                context.Request.Path.StartsWithSegments("/api/gym/students") &&
                context.Request.Path.Value?.Contains("/recovery-reservations", StringComparison.OrdinalIgnoreCase) == true)
            {
                context.Response.Headers.CacheControl = "no-store";
            }

            await context.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json");
        }
    }

    private static (int Status, string Title, string Detail) Map(Exception exception) => exception switch
    {
        AttendanceException attendance => (attendance.StatusCode,
            Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(attendance.StatusCode),
            attendance.StatusCode >= 500 ? "The operation could not be completed." : attendance.Message),
        DomainValidationException => (StatusCodes.Status400BadRequest, "Bad Request", exception.Message),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", exception.Message),
        NotFoundException => (StatusCodes.Status404NotFound, "Not Found", exception.Message),
        ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),
        PayloadTooLargeException => (StatusCodes.Status413PayloadTooLarge, "Payload Too Large", exception.Message),
        UpstreamServiceException => (StatusCodes.Status502BadGateway, "Bad Gateway", exception.Message),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.")
    };
}
