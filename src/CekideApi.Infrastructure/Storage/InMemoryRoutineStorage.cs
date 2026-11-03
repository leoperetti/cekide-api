using System.Collections.Concurrent;
using CekideApi.Application.Abstractions;

namespace CekideApi.Infrastructure.Storage;

/// <summary>Volatile storage used by tests and local runs without Azure/Azurite.</summary>
public sealed class InMemoryRoutineStorage : IRoutineStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public int Count => _files.Count;

    public bool Contains(string path) => _files.ContainsKey(path);

    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _files[path] = buffer.ToArray();
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(_files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
}
