using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using CekideApi.Configuration;
using CekideApi.Infrastructure.Jobs;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CekideApi.IntegrationTests;

public class EnvironmentConfigurationTests
{
    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Azure_environments_require_a_key_vault_name(string environmentName)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environmentName));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("KeyVault:Name", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Testing_starts_with_fake_configuration_without_azure_connections()
    {
        using var factory = new CekideApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Development_registers_user_secrets_and_ignores_key_vault()
    {
        using var factory = new ConfiguredApiFactory("Development", RequiredSettings());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/");
        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(
            ((IConfigurationRoot)configuration).Providers,
            provider => provider is JsonConfigurationProvider json && json.Source.Path == "secrets.json");
        Assert.DoesNotContain(
            ((IConfigurationRoot)configuration).Providers,
            provider => provider.GetType().Name == "AzureKeyVaultConfigurationProvider");
    }

    [Theory]
    [InlineData("Staging", "cekide-key-vault-prod")]
    [InlineData("Production", "cekide-key-vault-staging")]
    public void Azure_startup_rejects_another_environments_vault(string environmentName, string vaultName)
    {
        var settings = RequiredSettings();
        settings["KeyVault:Name"] = vaultName;
        using var factory = new ConfiguredApiFactory(environmentName, settings);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("KeyVault:Name", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("environment-configuration-test-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Development_startup_rejects_an_azure_database()
    {
        var settings = RequiredSettings();
        settings["CekideDatabaseConnectionString"] =
            "Server=cekide.database.windows.net;Database=cekide-db;Password=environment-configuration-test-secret";
        using var factory = new ConfiguredApiFactory("Development", settings);

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("CekideDatabaseConnectionString", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("environment-configuration-test-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Staging", "cekide-key-vault-staging", "cekide-db-staging")]
    [InlineData("Production", "cekide-key-vault-prod", "cekide-db")]
    public void Key_vault_overrides_json_environment_and_command_line_configuration(
        string environmentName, string vaultName, string databaseName)
    {
        var variablePrefix = $"CEKIDE_CONFIGURATION_TEST_{Guid.NewGuid():N}_";
        var variableName = variablePrefix + "Jwt__Key";
        Environment.SetEnvironmentVariable(variableName, "environment-test-value");
        try
        {
            var configuration = new ConfigurationManager();
            using var json = new MemoryStream(Encoding.UTF8.GetBytes("""{"Jwt":{"Key":"json-test-value"}}"""));
            configuration.AddJsonStream(json);
            configuration.AddEnvironmentVariables(variablePrefix);
            Assert.Equal("environment-test-value", configuration["Jwt:Key"]);
            configuration.AddCommandLine(["--Jwt:Key=command-line-test-value"]);
            Assert.Equal("command-line-test-value", configuration["Jwt:Key"]);

            var settings = RequiredSettings();
            settings.Remove("Jwt:Key");
            settings["KeyVault:Name"] = vaultName;
            settings["CekideDatabaseConnectionString"] =
                $"Server=cekide.database.windows.net;Database={databaseName};Authentication=Active Directory Managed Identity";
            configuration.AddInMemoryCollection(settings);
            using var handler = new KeyVaultHandler(vaultName);
            using var httpClient = new HttpClient(handler);
            var clientOptions = new SecretClientOptions
            {
                Transport = new HttpClientTransport(httpClient)
            };
            clientOptions.Retry.MaxRetries = 0;
            var client = new SecretClient(
                new Uri($"https://{vaultName}.vault.azure.net/"),
                new SyntheticCredential(),
                clientOptions);

            configuration.AddAzureKeyVault(client, new AzureKeyVaultConfigurationOptions());

            Assert.Equal("key-vault-synthetic-test-value", configuration["Jwt:Key"]);
            EnvironmentConfigurationValidator.ValidateRequiredSettings(configuration, environmentName);
            Assert.True(handler.RequestCount >= 2);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    private static Dictionary<string, string?> RequiredSettings() => new()
    {
        ["Jwt:Key"] = "environment-configuration-test-secret-with-sufficient-length",
        ["Jwt:Issuer"] = "cekide-tests",
        ["Jwt:Audience"] = "cekide-tests",
        ["CekideDatabaseConnectionString"] = "Server=(localdb)\\MSSQLLocalDB;Database=cekide-tests",
        ["Storage:Provider"] = "AzureBlob",
        ["Storage:AccountUrl"] = "https://cekideblobaccount.blob.core.windows.net",
        ["Brevo:ApiKey"] = "environment-configuration-test-secret",
        ["Authcraft:BaseUrl"] = "https://authcraft.example.com",
        ["KeyVault:Name"] = "nonexistent-vault.invalid"
    };

    private sealed class ConfiguredApiFactory(
        string environmentName,
        Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Host configuration reaches the entry point before its startup validation.
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(settings));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            builder.ConfigureServices(services =>
            {
                foreach (var service in services.Where(descriptor =>
                             descriptor.ServiceType == typeof(IHostedService) &&
                             descriptor.ImplementationType == typeof(OverdueNotificationJob)).ToArray())
                {
                    services.Remove(service);
                }
            });
        }
    }

    private sealed class SyntheticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("synthetic-token-not-valid-in-azure", DateTimeOffset.UtcNow.AddMinutes(5));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class KeyVaultHandler(string vaultName) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));

        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var id = $"https://{vaultName}.vault.azure.net/secrets/Jwt--Key";
            var path = request.RequestUri!.AbsolutePath.TrimEnd('/');
            var body = path switch
            {
                "/secrets" => JsonSerializer.Serialize(new
                {
                    value = new[] { new { id, attributes = new { enabled = true } } }
                }),
                "/secrets/Jwt--Key" => JsonSerializer.Serialize(new
                {
                    id = id + "/test-version",
                    value = "key-vault-synthetic-test-value",
                    attributes = new { enabled = true }
                }),
                _ => throw new InvalidOperationException("Unexpected request to the synthetic Key Vault transport.")
            };
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
