using Azure.Identity;
using CekideApi.Configuration;
using Microsoft.Extensions.Configuration;

namespace CekideApi.UnitTests;

public class EnvironmentKeyVaultConfigurationTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Local_and_test_environments_do_not_add_a_key_vault_provider(string environmentName)
    {
        var configuration = new ConfigurationManager
        {
            ["KeyVault:Name"] = "nonexistent-vault.invalid"
        };
        var credential = new DefaultAzureCredential();

        EnvironmentKeyVaultConfiguration.AddForEnvironment(
            configuration,
            environmentName,
            credential);

        Assert.DoesNotContain(
            ((IConfigurationRoot)configuration).Providers,
            provider => provider.GetType().Name == "AzureKeyVaultConfigurationProvider");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Azure_environments_require_a_key_vault_name(string environmentName)
    {
        var configuration = new ConfigurationManager();
        var credential = new DefaultAzureCredential();

        var exception = Assert.Throws<InvalidOperationException>(
            () => EnvironmentKeyVaultConfiguration.AddForEnvironment(
                configuration,
                environmentName,
                credential));

        Assert.Contains("KeyVault:Name", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Staging", "cekide-key-vault-prod")]
    [InlineData("Production", "cekide-key-vault-staging")]
    [InlineData("Staging", "environment-test-secret.invalid/path")]
    public void Foreign_or_invalid_vault_fails_before_requesting_a_token(
        string environmentName, string vaultName)
    {
        var configuration = new ConfigurationManager
        {
            ["KeyVault:Name"] = vaultName
        };
        var credential = new RejectingCredential();

        var exception = Assert.Throws<InvalidOperationException>(
            () => EnvironmentKeyVaultConfiguration.AddForEnvironment(
                configuration, environmentName, credential));

        Assert.Contains("KeyVault:Name", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(vaultName, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, credential.RequestCount);
    }

    private sealed class RejectingCredential : Azure.Core.TokenCredential
    {
        public int RequestCount { get; private set; }

        public override Azure.Core.AccessToken GetToken(
            Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("A test must not request Azure tokens.");
        }

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(
            Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
