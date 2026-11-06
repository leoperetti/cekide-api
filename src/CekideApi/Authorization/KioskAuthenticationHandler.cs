using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CekideApi.Application.Abstractions;
using CekideApi.Application.Kiosks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CekideApi.Authorization;

public sealed class KioskAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Kiosk";
    public const string Role = "kiosk";
    public const string SessionIdClaim = "kiosk_session_id";

    private readonly IKioskSessionRepository _sessions;
    private readonly IClock _clock;
    private readonly string _tenant;

    public KioskAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IKioskSessionRepository sessions,
        IClock clock,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _sessions = sessions;
        _clock = clock;
        _tenant = configuration["Tenant:Id"] is { Length: > 0 } configured
            ? configured
            : TenantRequirementHandler.DefaultTenantId;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) ||
            !string.Equals(header.Scheme, SchemeName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return AuthenticateResult.Fail("Kiosk credentials are invalid.");
        }

        var tokenHash = KioskSessionService.Hash(header.Parameter);
        var session = await _sessions.GetByTokenHashAsync(tokenHash, Context.RequestAborted);
        if (session is null ||
            !string.Equals(session.Tenant, _tenant, StringComparison.OrdinalIgnoreCase) ||
            !session.HasValidTokenHash(tokenHash, _clock.UtcNow))
        {
            return AuthenticateResult.Fail("Kiosk credentials are invalid.");
        }

        var claims = new[]
        {
            new Claim(SessionIdClaim, session.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(TenantRequirementHandler.TenantClaim, session.Tenant),
            new Claim(ClaimTypes.Role, Role)
        };
        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.CacheControl = "no-store";
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Detail = "Kiosk session is not authorized."
        };
        problem.Extensions["code"] = "kiosk_not_authorized";
        await Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
