using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.Extensions.Configuration;

namespace CekideApi.Configuration;

public static class EnvironmentKeyVaultConfiguration
{
    public static void AddForEnvironment(
        IConfigurationManager configuration,
        string environmentName,
        TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        ArgumentNullException.ThrowIfNull(credential);

        if (!environmentName.Equals("Staging", StringComparison.OrdinalIgnoreCase) &&
            !environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var keyVaultName = configuration["KeyVault:Name"];
        if (string.IsNullOrWhiteSpace(keyVaultName))
        {
            throw new InvalidOperationException("Configuration key 'KeyVault:Name' is required in Staging and Production.");
        }

        var expectedVaultName = environmentName.Equals("Staging", StringComparison.OrdinalIgnoreCase)
            ? "cekide-key-vault-staging"
            : "cekide-key-vault-prod";
        if (!keyVaultName.Equals(expectedVaultName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Configuration key 'KeyVault:Name' must identify the vault assigned to this environment.");
        }

        configuration.AddAzureKeyVault(new Uri($"https://{keyVaultName}.vault.azure.net/"), credential);
    }
}
