using CekideApi.Configuration;
using Microsoft.Extensions.Configuration;

namespace CekideApi.UnitTests;

public class EnvironmentConfigurationValidatorTests
{
    private const string TestSecret = "environment-validation-test-secret";

    [Fact]
    public void Development_requires_local_configuration_without_a_key_vault()
    {
        var configuration = BuildConfiguration(RequiredSettings());

        EnvironmentConfigurationValidator.ValidateRequiredSettings(configuration, "Development");
    }

    [Fact]
    public void Missing_required_configuration_fails_without_exposing_other_values()
    {
        var settings = RequiredSettings();
        settings["Brevo:ApiKey"] = null;
        var configuration = BuildConfiguration(settings);

        var exception = Assert.Throws<InvalidOperationException>(
            () => { EnvironmentConfigurationValidator.ValidateRequiredSettings(configuration, "Staging"); });

        Assert.Contains("Brevo:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Testing_does_not_require_azure_configuration()
    {
        var configuration = BuildConfiguration([]);

        EnvironmentConfigurationValidator.ValidateRequiredSettings(configuration, "Testing");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Correct_environment_destinations_are_accepted(string environmentName)
    {
        var configuration = BuildConfiguration(RequiredSettings(environmentName));

        EnvironmentConfigurationValidator.ValidateRequiredSettings(configuration, environmentName);
    }

    [Theory]
    [InlineData("Development", "Server=cekide.database.windows.net;Database=cekide-db")]
    [InlineData("Staging", "Server=cekide.database.windows.net;Database=cekide-db;Authentication=Active Directory Managed Identity")]
    [InlineData("Production", "Server=cekide.database.windows.net;Database=cekide-db-staging;Authentication=Active Directory Managed Identity")]
    [InlineData("Staging", "Server=other.database.windows.net;Database=cekide-db-staging;Authentication=Active Directory Managed Identity")]
    [InlineData("Production", "Server=(localdb)\\MSSQLLocalDB;Database=cekide-db")]
    [InlineData("Staging", "Server=cekide.database.windows.net;Database=cekide-db-staging;User ID=user;Password=environment-validation-test-secret")]
    [InlineData("Production", "Server=cekide.database.windows.net;Database=cekide-db;Authentication=Active Directory Managed Identity;User ID=other-identity")]
    [InlineData("Staging", "Server=cekide.database.windows.net;Database=cekide-db-staging;Authentication=Active Directory Managed Identity;TrustServerCertificate=True")]
    [InlineData("Staging", "Server=cekide.database.windows.net;Database=cekide-db-staging;Authentication=Active Directory Managed Identity;Encrypt=False")]
    [InlineData("Staging", "environment-validation-test-secret=invalid")]
    public void Foreign_or_unsafe_sql_destinations_fail_without_exposing_values(
        string environmentName, string connectionString)
    {
        var settings = RequiredSettings(environmentName);
        settings["CekideDatabaseConnectionString"] = connectionString;

        var exception = Assert.Throws<InvalidOperationException>(
            () => EnvironmentConfigurationValidator.ValidateRequiredSettings(
                BuildConfiguration(settings), environmentName));

        Assert.Contains("CekideDatabaseConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", "https://other.blob.core.windows.net")]
    [InlineData("Staging", "https://cekideblobaccount.blob.core.windows.net:8443")]
    [InlineData("Production", "https://cekideblobaccount.blob.core.windows.net/?sig=environment-validation-test-secret")]
    [InlineData("Development", "https://cekidecontainer.blob.core.windows.net")]
    public void Foreign_or_unsafe_blob_endpoints_fail_without_exposing_values(
        string environmentName, string accountUrl)
    {
        var settings = RequiredSettings(environmentName);
        settings["Storage:AccountUrl"] = accountUrl;

        var exception = Assert.Throws<InvalidOperationException>(
            () => EnvironmentConfigurationValidator.ValidateRequiredSettings(
                BuildConfiguration(settings), environmentName));

        Assert.Contains("Storage:AccountUrl", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(accountUrl, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Storage:Provider", "Azurite")]
    [InlineData("Storage:ConnectionString", "environment-validation-test-secret")]
    [InlineData("Storage:AzuriteConnectionString", "environment-validation-test-secret")]
    public void Runtime_rejects_emulator_or_shared_key_configuration(string key, string value)
    {
        var settings = RequiredSettings("Staging");
        settings[key] = value;

        var exception = Assert.Throws<InvalidOperationException>(
            () => EnvironmentConfigurationValidator.ValidateRequiredSettings(
                BuildConfiguration(settings), "Staging"));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecret, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Staging", "cekide-db-staging")]
    [InlineData("Production", "cekide-db")]
    public void Explicit_sql_tcp_port_is_accepted(string environmentName, string databaseName)
    {
        var settings = RequiredSettings(environmentName);
        settings["CekideDatabaseConnectionString"] =
            $"Server=tcp:cekide.database.windows.net,1433;Database={databaseName};Authentication=Active Directory Managed Identity";

        EnvironmentConfigurationValidator.ValidateRequiredSettings(BuildConfiguration(settings), environmentName);
    }

    [Theory]
    [InlineData("Jwt:Key")]
    [InlineData("Jwt:Issuer")]
    [InlineData("Jwt:Audience")]
    [InlineData("CekideDatabaseConnectionString")]
    [InlineData("Storage:AccountUrl")]
    [InlineData("Brevo:ApiKey")]
    [InlineData("Authcraft:BaseUrl")]
    public void Every_required_setting_fails_closed_in_both_azure_environments(string key)
    {
        foreach (var environmentName in new[] { "Staging", "Production" })
        {
            var settings = RequiredSettings(environmentName);
            settings[key] = " ";

            var exception = Assert.Throws<InvalidOperationException>(
                () => EnvironmentConfigurationValidator.ValidateRequiredSettings(
                    BuildConfiguration(settings), environmentName));

            Assert.Contains(key, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(TestSecret, exception.ToString(), StringComparison.Ordinal);
        }
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Dictionary<string, string?> RequiredSettings(string environmentName = "Development") => new()
    {
        ["Jwt:Key"] = TestSecret,
        ["Jwt:Issuer"] = "cekide-tests",
        ["Jwt:Audience"] = "cekide-tests",
        ["CekideDatabaseConnectionString"] = environmentName switch
        {
            "Staging" => "Server=cekide.database.windows.net;Database=cekide-db-staging;Authentication=Active Directory Managed Identity",
            "Production" => "Server=cekide.database.windows.net;Database=cekide-db;Authentication=Active Directory Managed Identity",
            _ => "Server=(localdb)\\MSSQLLocalDB;Database=cekide-tests"
        },
        ["Storage:Provider"] = "AzureBlob",
        ["Storage:AccountUrl"] = "https://cekideblobaccount.blob.core.windows.net",
        ["Brevo:ApiKey"] = TestSecret,
        ["Authcraft:BaseUrl"] = "https://authcraft.example.com"
    };
}
