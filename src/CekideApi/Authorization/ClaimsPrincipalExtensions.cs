using System.Net.Http.Headers;
using System.Security.Claims;
using CekideApi.Domain;

namespace CekideApi.Authorization;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authcraft identity id ("sub"); JwtBearer may map it to NameIdentifier.</summary>
    public static string? GetSubject(this ClaimsPrincipal user) =>
        user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    public static string GetActor(this ClaimsPrincipal user) => user.GetSubject() ?? "unknown";

    /// <summary>The account email; JwtBearer maps the "email" claim to ClaimTypes.Email.</summary>
    public static string? GetEmail(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");

    /// <summary>The caller's access token, forwarded to authcraft-api when acting on their behalf.</summary>
    public static string GetBearerToken(this HttpRequest request) =>
        AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header)
        && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(header.Parameter)
            ? header.Parameter
            : throw new ForbiddenException("A bearer token is required.");
}
