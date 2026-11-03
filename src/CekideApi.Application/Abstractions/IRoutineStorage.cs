namespace CekideApi.Application.Abstractions;

/// <summary>Private storage for the current routine sheet of each student.</summary>
public interface IRoutineStorage
{
    /// <summary>Stores (or overwrites) the content at the given path.</summary>
    Task SaveAsync(string path, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Opens the stored content, or returns null when nothing is stored at the path.</summary>
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default);
}
