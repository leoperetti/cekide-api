using CekideApi.Infrastructure;
using CekideApi.Infrastructure.Storage;
using Microsoft.Data.SqlClient;

namespace CekideApi.Configuration;

public static class EnvironmentConfigurationValidator
{
    public static void ValidateRequiredSettings(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        if (environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var requiredSettings = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = configuration["JwtKey"] ?? configuration["Jwt:Key"],
            ["Jwt:Issuer"] = configuration["Jwt:Issuer"],
            ["Jwt:Audience"] = configuration["Jwt:Audience"],
            [DependencyInjection.ConnectionStringKey] =
                configuration[DependencyInjection.ConnectionStringKey]
                ?? configuration.GetConnectionString(DependencyInjection.ConnectionStringKey),
            ["Storage:AccountUrl"] = configuration["Storage:AccountUrl"],
            ["Brevo:ApiKey"] = configuration["Brevo:ApiKey"],
            ["Authcraft:BaseUrl"] = configuration["Authcraft:BaseUrl"]
        };

        foreach (var (key, value) in requiredSettings)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Configuration key '{key}' is required for environment '{environmentName}'.");
            }
        }

        ValidateDatabase(requiredSettings[DependencyInjection.ConnectionStringKey]!, environmentName);
        ValidateStorage(configuration, requiredSettings["Storage:AccountUrl"]);
    }

    private static void ValidateDatabase(string connectionString, string environmentName)
    {
        SqlConnectionStringBuilder target;
        try
        {
            target = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // Parser errors can contain credential values embedded in invalid keywords.
            throw new InvalidOperationException($"Configuration key '{DependencyInjection.ConnectionStringKey}' must be a valid SQL Server connection string.");
        }

        if (environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            const string localDbPrefix = "(localdb)\\";
            if (!target.DataSource.StartsWith(localDbPrefix, StringComparison.OrdinalIgnoreCase)
                || target.DataSource.Length == localDbPrefix.Length
                || string.IsNullOrWhiteSpace(target.InitialCatalog)
                || !string.IsNullOrWhiteSpace(target.FailoverPartner))
            {
                throw new InvalidOperationException($"Configuration key '{DependencyInjection.ConnectionStringKey}' must target a local LocalDB database in Development.");
            }

            return;
        }

        var expectedDatabase = environmentName.ToLowerInvariant() switch
        {
            "staging" => "cekide-db-staging",
            "production" => "cekide-db",
            _ => throw new InvalidOperationException("The configured application environment is not supported.")
        };
        var server = target.DataSource;
        if (server.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            server = server[4..];
        }
        if (server.EndsWith(",1433", StringComparison.Ordinal))
        {
            server = server[..^5];
        }

        if (!server.Equals("cekide.database.windows.net", StringComparison.OrdinalIgnoreCase)
            || !target.InitialCatalog.Equals(expectedDatabase, StringComparison.OrdinalIgnoreCase)
            || target.Authentication != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity
            || target.IntegratedSecurity
            || !string.IsNullOrEmpty(target.UserID)
            || !string.IsNullOrEmpty(target.Password)
            || !string.IsNullOrEmpty(target.FailoverPartner)
            || !string.IsNullOrEmpty(target.AttachDBFilename)
            || target.Encrypt == SqlConnectionEncryptOption.Optional
            || target.TrustServerCertificate)
        {
            throw new InvalidOperationException($"Configuration key '{DependencyInjection.ConnectionStringKey}' must use the environment's assigned Azure SQL database with system-assigned Managed Identity and verified TLS.");
        }
    }

    private static void ValidateStorage(IConfiguration configuration, string? accountUrl)
    {
        foreach (var key in new[] { "Storage:ConnectionString", "Storage:AzuriteConnectionString" })
        {
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                throw new InvalidOperationException($"Configuration key '{key}' must not be set outside Testing.");
            }
        }

        if (configuration["Storage:Provider"] is { } provider && provider != "AzureBlob")
        {
            throw new InvalidOperationException("Configuration key 'Storage:Provider' must be 'AzureBlob' outside Testing.");
        }

        var endpoint = StorageOptions.ParseAccountUrl(accountUrl);
        if (!endpoint.Host.Equals("cekideblobaccount.blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
            || !endpoint.IsDefaultPort)
        {
            throw new InvalidOperationException("Configuration key 'Storage:AccountUrl' must identify the shared cekideblobaccount Azure Blob account.");
        }
    }
}
