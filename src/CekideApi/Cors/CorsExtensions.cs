using Microsoft.AspNetCore.Cors.Infrastructure;

namespace CekideApi.Cors;

public static class CorsExtensions
{
    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    /// <summary>Allows only the configured tenant origins (Cors:AllowedOrigins) and localhost.</summary>
    public static IServiceCollection AddCekideCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var allowed = (configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [])
                    .Select(Normalize)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                options.AddDefaultPolicy(policy => policy
                    .SetIsOriginAllowed(origin => allowed.Contains(Normalize(origin)) || IsLocalhost(origin))
                    .AllowAnyHeader()
                    .AllowAnyMethod());
            });

        return services;
    }

    private static string Normalize(string origin) => origin.Trim().TrimEnd('/');

    private static bool IsLocalhost(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && (uri.Host == "localhost" || uri.Host == "127.0.0.1");
}
