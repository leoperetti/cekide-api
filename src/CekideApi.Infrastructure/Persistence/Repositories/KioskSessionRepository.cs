using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence.Repositories;

public sealed class KioskSessionRepository(CekideDbContext context) : IKioskSessionRepository
{
    public Task<KioskSession?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        context.KioskSessions.FirstOrDefaultAsync(session => session.Id == id, cancellationToken);

    public Task<KioskSession?> GetByActivationCodeHashAsync(string hash, CancellationToken cancellationToken = default) =>
        context.KioskSessions.FirstOrDefaultAsync(session => session.ActivationCodeHash == hash, cancellationToken);

    public Task<KioskSession?> GetByTokenHashAsync(string hash, CancellationToken cancellationToken = default) =>
        context.KioskSessions.FirstOrDefaultAsync(session => session.TokenHash == hash, cancellationToken);

    public async Task<IReadOnlyList<KioskSession>> ListForTenantAsync(
        string tenant,
        CancellationToken cancellationToken = default) =>
        await context.KioskSessions.AsNoTracking()
            .Where(session => session.Tenant == tenant)
            .OrderByDescending(session => session.CreatedAt)
            .ThenByDescending(session => session.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> IsRevokedAsync(int id, string tenant, CancellationToken cancellationToken = default) =>
        context.KioskSessions.AsNoTracking()
            .AnyAsync(session => session.Id == id && session.Tenant == tenant && session.RevokedAt != null, cancellationToken);

    public void Add(KioskSession session) => context.KioskSessions.Add(session);
}
