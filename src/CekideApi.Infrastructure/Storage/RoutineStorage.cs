using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CekideApi.Application.Abstractions;

namespace CekideApi.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "AzureBlob";

    public string? AccountUrl { get; set; }

    public string? AzuriteConnectionString { get; set; }

    public static Uri ParseAccountUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var accountUrl)
            || accountUrl.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(accountUrl.UserInfo)
            || !string.IsNullOrEmpty(accountUrl.Query)
            || !string.IsNullOrEmpty(accountUrl.Fragment)
            || accountUrl.AbsolutePath is not ("" or "/"))
        {
            throw new InvalidOperationException("Configuration key 'Storage:AccountUrl' must be an HTTPS account endpoint without credentials or query values.");
        }

        return accountUrl;
    }
}

public sealed class EnvironmentStorageTarget
{
    public const string SharedContainerName = "cekidecontainer";

    private static readonly string[] EnvironmentPrefixes = ["development/", "staging/", "production/"];

    private EnvironmentStorageTarget(string prefix)
    {
        Prefix = prefix;
    }

    public string ContainerName => SharedContainerName;

    public string Prefix { get; }

    public static EnvironmentStorageTarget For(string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        return environmentName.Trim().ToLowerInvariant() switch
        {
            "development" => new EnvironmentStorageTarget("development/"),
            "staging" => new EnvironmentStorageTarget("staging/"),
            "production" => new EnvironmentStorageTarget("production/"),
            _ => throw new ArgumentException($"Unsupported storage environment '{environmentName}'.", nameof(environmentName))
        };
    }

    public string GetBlobName(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath)
            || relativePath.Contains('\\')
            || relativePath.Contains(':')
            || relativePath.Any(char.IsControl)
            || EnvironmentPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Routine storage paths must be relative and unprefixed.", nameof(relativePath));
        }

        var segments = relativePath.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("Routine storage paths must contain valid relative segments.", nameof(relativePath));
        }

        return Prefix + relativePath;
    }
}

/// <summary>Private Azure Blob container; files are only reachable through the authenticated API.</summary>
public sealed class AzureBlobRoutineStorage(BlobContainerClient container, EnvironmentStorageTarget target) : IRoutineStorage
{
    public static AzureBlobRoutineStorage Create(StorageOptions options, string environmentName, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);

        var accountUrl = StorageOptions.ParseAccountUrl(options.AccountUrl);
        var target = EnvironmentStorageTarget.For(environmentName);
        var service = new BlobServiceClient(accountUrl, credential);
        return new AzureBlobRoutineStorage(service.GetBlobContainerClient(target.ContainerName), target);
    }

    public static AzureBlobRoutineStorage CreateAzurite(StorageOptions options, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        if (!environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Azurite storage is only allowed in the Testing environment.");
        }

        if (string.IsNullOrWhiteSpace(options.AzuriteConnectionString))
        {
            throw new InvalidOperationException("Configuration key 'Storage:AzuriteConnectionString' is required for the Azurite test provider.");
        }

        var target = EnvironmentStorageTarget.For("Development");
        var container = new BlobContainerClient(options.AzuriteConnectionString, target.ContainerName);
        return new AzureBlobRoutineStorage(container, target);
    }

    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var blob = container.GetBlobClient(target.GetBlobName(path));
        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await container.GetBlobClient(target.GetBlobName(path)).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }
}
