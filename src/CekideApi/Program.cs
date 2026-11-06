using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Core;
using Azure.Identity;
using CekideApi.Application;
using CekideApi.Authorization;
using CekideApi.Configuration;
using CekideApi.Cors;
using CekideApi.Infrastructure;
using CekideApi.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

static string KioskRateLimitPartition(HttpContext context)
{
    var sessionId = context.User.FindFirstValue(KioskAuthenticationHandler.SessionIdClaim);
    if (!string.IsNullOrWhiteSpace(sessionId))
    {
        return sessionId;
    }

    return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

var builder = WebApplication.CreateBuilder(args);
TokenCredential credential = builder.Environment.IsDevelopment()
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential(new ManagedIdentityCredentialOptions());

EnvironmentKeyVaultConfiguration.AddForEnvironment(
    builder.Configuration,
    builder.Environment.EnvironmentName,
    credential);

EnvironmentConfigurationValidator.ValidateRequiredSettings(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});
builder.Services.AddApplication();
builder.Services.AddSingleton(credential);
builder.Services.AddInfrastructure();
builder.Services.AddCekideAuthentication();
builder.Services.AddCekideAuthorization();
builder.Services.AddCekideCors();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("kiosk-activation", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("kiosk-check-in", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            KioskRateLimitPartition(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.HttpContext.Response.Headers["Retry-After"] =
            context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "60";
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too Many Requests",
            Detail = "Wait before trying again."
        };
        problem.Extensions["code"] = "rate_limited";
        await context.HttpContext.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
    };
});

var app = builder.Build();

app.Services.ValidateAuthenticationConfiguration();

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.Run();

public partial class Program;
