using CekideApi.Domain;

namespace CekideApi.Application.Abstractions;

public interface IAttendanceQuotaNotificationRepository
{
    Task<AttendanceQuotaNotification?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceQuotaNotification>> ListAsync(
        string? period,
        AttendanceQuotaNotificationStatus? status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> ListRunnableIdsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default);

    Task<int> MarkExpiredClaimsAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<bool> TryClaimAsync(
        int id,
        string claimId,
        DateTimeOffset claimedAt,
        TimeSpan lease,
        CancellationToken cancellationToken = default);

    Task<bool> MarkSentAsync(
        int id,
        string claimId,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(
        int id,
        string claimId,
        string error,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default);

    Task<bool> MarkDeliveryUnknownAsync(
        int id,
        string claimId,
        string error,
        CancellationToken cancellationToken = default);

    Task<bool> QueueManualRetryAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> ResolveDeliveryAsync(
        int id,
        bool delivered,
        string resolvedBy,
        string evidence,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken = default);
}
