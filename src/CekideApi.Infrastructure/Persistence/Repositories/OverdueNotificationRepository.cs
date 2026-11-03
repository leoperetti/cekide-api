using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence.Repositories;

public sealed class OverdueNotificationRepository(CekideDbContext context) : IOverdueNotificationRepository
{
    public Task<OverdueNotification?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        context.OverdueNotifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<IReadOnlyList<OverdueNotification>> ListByPeriodAsync(string period, CancellationToken cancellationToken = default) =>
        await context.OverdueNotifications.Where(n => n.Period == period).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OverdueNotification>> ListAsync(
        string? period, NotificationStatus? status, CancellationToken cancellationToken = default)
    {
        var query = context.OverdueNotifications.AsQueryable();
        if (period is not null)
        {
            query = query.Where(n => n.Period == period);
        }

        if (status is { } wanted)
        {
            query = query.Where(n => n.Status == wanted);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public void Add(OverdueNotification notification) => context.OverdueNotifications.Add(notification);

    public void Discard(OverdueNotification notification) => context.Entry(notification).State = EntityState.Detached;
}
