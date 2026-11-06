using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CekideApi.Authorization;

public static class AuthenticationExtensions
{
    /// <summary>Validates JWTs issued by authcraft-api (same Jwt:Key / Issuer / Audience, role claim = ClaimTypes.Role).</summary>
    public static IServiceCollection AddCekideAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, KioskAuthenticationHandler>(
                KioskAuthenticationHandler.SchemeName, _ => { });

        // Configuration is read lazily so values added after Program.cs starts (Key Vault, tests) are honoured.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, configuration) =>
            {
                var jwtKey = configuration["JwtKey"] ?? configuration["Jwt:Key"];
                if (string.IsNullOrWhiteSpace(jwtKey))
                {
                    throw new InvalidOperationException("Configuration key 'Jwt:Key' is required (user-secrets or Key Vault).");
                }

                var issuer = configuration["Jwt:Issuer"];
                var audience = configuration["Jwt:Audience"];

                options.MapInboundClaims = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateIssuer = !string.IsNullOrEmpty(issuer),
                    ValidIssuer = string.IsNullOrEmpty(issuer) ? null : issuer,
                    ValidateAudience = !string.IsNullOrEmpty(audience),
                    ValidAudience = string.IsNullOrEmpty(audience) ? null : audience,
                    ValidateLifetime = true,
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };
            });

        return services;
    }

    /// <summary>Fails fast at startup when the JWT configuration is incomplete.</summary>
    public static void ValidateAuthenticationConfiguration(this IServiceProvider services) =>
        services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
}
