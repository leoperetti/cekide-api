using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CekideApi.Application.Abstractions;

namespace CekideApi.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string? ConnectionString { get; set; }

    public string RoutinesContainer { get; set; } = "routines";
}

/// <summary>Private Azure Blob container; files are only reachable through the authenticated API.</summary>
public sealed class AzureBlobRoutineStorage(BlobContainerClient container) : IRoutineStorage
{
    private bool _containerReady;

    public static AzureBlobRoutineStorage Create(StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("Configuration key 'Storage:ConnectionString' is required for routine storage.");
        }

        return new AzureBlobRoutineStorage(new BlobContainerClient(options.ConnectionString, options.RoutinesContainer));
    }

    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(path);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync(cancellationToken);
        try
        {
            return await container.GetBlobClient(path).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerReady)
        {
            return;
        }

        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        _containerReady = true;
    }
}
