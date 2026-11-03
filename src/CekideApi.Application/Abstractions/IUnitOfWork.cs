namespace CekideApi.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>Persists pending changes; unique-constraint violations surface as ConflictException.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs several saves atomically (used when an audit row needs the generated id of the audited row).</summary>
    Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
