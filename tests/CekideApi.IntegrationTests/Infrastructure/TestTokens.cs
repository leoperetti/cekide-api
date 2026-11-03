using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace CekideApi.IntegrationTests.Infrastructure;

/// <summary>Issues JWTs shaped like the ones authcraft-api emits (sub, email, tenant, ClaimTypes.Role).</summary>
public static class TestTokens
{
    public const string Key = "integration-tests-signing-key-0123456789-abcdef";
    public const string Issuer = "authcraft-tests";
    public const string Audience = "cekide-tests";
    public const string Tenant = "cekide-app";

    public static string Create(
        string? role,
        string subject = "user-1",
        string? tenant = Tenant,
        string? signingKey = null,
        string? issuer = Issuer,
        DateTime? expires = null,
        params string[] extraRoles)
    {
        var claims = new List<Claim>
        {
            new("sub", subject),
            new(ClaimTypes.Email, $"{subject}@example.com"),
            new(ClaimTypes.Name, subject)
        };

        if (tenant is not null)
        {
            claims.Add(new Claim("tenant", tenant));
        }

        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        claims.AddRange(extraRoles.Select(extra => new Claim(ClaimTypes.Role, extra)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Audience = Audience,
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? Key)),
                SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
