using CekideApi.Application.Abstractions;
using CekideApi.Application.Notifications;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence.Repositories;

public sealed class AttendanceQuotaNotificationRepository(CekideDbContext context, IClock clock)
    : IAttendanceQuotaNotificationRepository
{
    public Task<AttendanceQuotaNotification?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        context.AttendanceQuotaNotifications.AsNoTracking().FirstOrDefaultAsync(
            notification => notification.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AttendanceQuotaNotification>> ListAsync(
        string? period,
        AttendanceQuotaNotificationStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = context.AttendanceQuotaNotifications.AsNoTracking().AsQueryable();
        if (period is not null)
        {
            query = query.Where(notification => notification.Period == period);
        }

        if (status is { } selectedStatus)
        {
            query = query.Where(notification => notification.Status == selectedStatus);
        }

        return await query
            .OrderByDescending(notification => notification.Period)
            .ThenByDescending(notification => notification.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> ListRunnableIdsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var utcNow = now.UtcDateTime;
        return await context.AttendanceQuotaNotifications.AsNoTracking()
            .Where(notification =>
                notification.Status == AttendanceQuotaNotificationStatus.Pending ||
                notification.Status == AttendanceQuotaNotificationStatus.Failed &&
                notification.Attempts <= AttendanceQuotaNotificationPolicy.MaximumAutomaticRetries &&
                notification.NextAttemptAt <= utcNow)
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .Select(notification => notification.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<int> MarkExpiredClaimsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var utcNow = now.UtcDateTime;
        return context.AttendanceQuotaNotifications
            .Where(notification =>
                notification.Status == AttendanceQuotaNotificationStatus.Sending &&
                notification.ClaimedUntil != null &&
                notification.ClaimedUntil <= utcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.Status, AttendanceQuotaNotificationStatus.DeliveryUnknown)
                .SetProperty(notification => notification.Attempts, notification => notification.Attempts + 1)
                .SetProperty(
                    notification => notification.LastError,
                    AttendanceQuotaNotification.ExpiredClaimError)
                .SetProperty(notification => notification.ClaimId, (string?)null)
                .SetProperty(notification => notification.ClaimedUntil, (DateTime?)null)
                .SetProperty(notification => notification.UpdatedAt, utcNow), cancellationToken);
    }

    public async Task<bool> TryClaimAsync(
        int id,
        string claimId,
        DateTimeOffset claimedAt,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        var now = claimedAt.UtcDateTime;
        var claimedUntil = now.Add(lease);
        return await context.AttendanceQuotaNotifications
            .Where(notification =>
                notification.Id == id &&
                (notification.Status == AttendanceQuotaNotificationStatus.Pending ||
                 notification.Status == AttendanceQuotaNotificationStatus.Failed &&
                 notification.Attempts <= AttendanceQuotaNotificationPolicy.MaximumAutomaticRetries &&
                 notification.NextAttemptAt <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.Status, AttendanceQuotaNotificationStatus.Sending)
                .SetProperty(notification => notification.ClaimId, claimId)
                .SetProperty(notification => notification.ClaimedUntil, claimedUntil)
                .SetProperty(notification => notification.NextAttemptAt, (DateTime?)null)
                .SetProperty(notification => notification.UpdatedAt, now), cancellationToken) == 1;
    }

    public async Task<bool> MarkSentAsync(
        int id,
        string claimId,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default) =>
        await context.AttendanceQuotaNotifications
            .Where(notification =>
                notification.Id == id &&
                notification.Status == AttendanceQuotaNotificationStatus.Sending &&
                notification.ClaimId == claimId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.Status, AttendanceQuotaNotificationStatus.Sent)
                .SetProperty(notification => notification.Attempts, notification => notification.Attempts + 1)
                .SetProperty(notification => notification.SentAt, sentAt.UtcDateTime)
                .SetProperty(notification => notification.LastError, (string?)null)
                .SetProperty(notification => notification.ClaimId, (string?)null)
                .SetProperty(notification => notification.ClaimedUntil, (DateTime?)null)
                .SetProperty(notification => notification.UpdatedAt, sentAt.UtcDateTime), cancellationToken) == 1;

    public async Task<bool> MarkFailedAsync(
        int id,
        string claimId,
        string error,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default) =>
        await context.AttendanceQuotaNotifications
            .Where(notification =>
                notification.Id == id &&
                notification.Status == AttendanceQuotaNotificationStatus.Sending &&
                notification.ClaimId == claimId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.Status, AttendanceQuotaNotificationStatus.Failed)
                .SetProperty(notification => notification.Attempts, notification => notification.Attempts + 1)
                .SetProperty(notification => notification.NextAttemptAt, retryAt.HasValue ? retryAt.Value.UtcDateTime : null)
                .SetProperty(notification => notification.LastError, error)
                .SetProperty(notification => notification.ClaimId, (string?)null)
                .SetProperty(notification => notification.ClaimedUntil, (DateTime?)null)
                .SetProperty(notification => notification.UpdatedAt, clock.UtcNow.UtcDateTime), cancellationToken) == 1;

    public async Task<bool> MarkDeliveryUnknownAsync(
        int id,
        string claimId,
        string error,
        CancellationToken cancellationToken = default) =>
        await context.AttendanceQuotaNotifications
            .Where(notification =>
                notification.Id == id &&
                notification.Status == AttendanceQuotaNotificationStatus.Sending &&
                notification.ClaimId == claimId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.Status, AttendanceQuotaNotificationStatus.DeliveryUnknown)
                .SetProperty(notification => notification.Attempts, notification => notification.Attempts + 1)
                .SetProperty(notification => notification.LastError, error)
                .SetProperty(notification => notification.ClaimId, (string?)null)
                .SetProperty(notification => notification.ClaimedUntil, (DateTime?)null)
                .SetProperty(notification => notification.UpdatedAt, clock.UtcNow.UtcDateTime), cancellationToken) == 1;

    public async Task<bool> QueueManualRetryAsync(int id, CancellationToken cancellationToken = default) =>
        await context.AttendanceQuotaNotifications
            .Where(notification =>
                notification.Id == id &&
                notification.Status == AttendanceQuotaNotificationStatus.Failed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.Status, AttendanceQuotaNotificationStatus.Pending)
                .SetProperty(notification => notification.NextAttemptAt, (DateTime?)null)
                .SetProperty(notification => notification.UpdatedAt, clock.UtcNow.UtcDateTime), cancellationToken) == 1;

    public async Task<bool> ResolveDeliveryAsync(
        int id,
        bool delivered,
        string resolvedBy,
        string evidence,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken = default) =>
        await context.AttendanceQuotaNotifications
            .Where(notification =>
                notification.Id == id &&
                notification.Status == AttendanceQuotaNotificationStatus.DeliveryUnknown)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    notification => notification.Status,
                    delivered ? AttendanceQuotaNotificationStatus.Sent : AttendanceQuotaNotificationStatus.Failed)
                .SetProperty(notification => notification.ResolvedBy, resolvedBy)
                .SetProperty(notification => notification.ResolvedAt, resolvedAt.UtcDateTime)
                .SetProperty(notification => notification.ResolutionEvidence, evidence)
                .SetProperty(
                    notification => notification.LastError,
                    notification => delivered
                        ? (string?)null
                        : AttendanceQuotaNotification.ConfirmedNotSentError)
                .SetProperty(
                    notification => notification.SentAt,
                    notification => delivered ? resolvedAt.UtcDateTime : notification.SentAt)
                .SetProperty(
                    notification => notification.NextAttemptAt,
                    (DateTime?)null)
                .SetProperty(notification => notification.UpdatedAt, resolvedAt.UtcDateTime), cancellationToken) == 1;

}
