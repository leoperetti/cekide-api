using CekideApi.Domain;

namespace CekideApi.Application.Abstractions;

public interface IOverdueNotificationRepository
{
    Task<OverdueNotification?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OverdueNotification>> ListByPeriodAsync(string period, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OverdueNotification>> ListAsync(string? period, NotificationStatus? status, CancellationToken cancellationToken = default);

    void Add(OverdueNotification notification);

    /// <summary>Forgets a notification whose insert was rejected so later saves do not retry it.</summary>
    void Discard(OverdueNotification notification);
}
