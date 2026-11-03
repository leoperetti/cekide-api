using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence.Repositories;

public sealed class PlanRepository(CekideDbContext context) : IPlanRepository
{
    public Task<Plan?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        context.Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Plans.OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Plan>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return idList.Count == 0
            ? []
            : await context.Plans.Where(p => idList.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public async Task<bool> NameExistsAsync(string name, int? exceptPlanId, CancellationToken cancellationToken = default)
    {
        // The plans table is tiny; comparing in memory keeps the check case/accent-insensitive on every provider.
        var existing = await context.Plans.AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        return existing.Any(p => p.Id != exceptPlanId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public void Add(Plan plan) => context.Plans.Add(plan);
}

public sealed class PassTariffRepository(CekideDbContext context) : IPassTariffRepository
{
    public async Task<IReadOnlyList<PassTariff>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.PassTariffs.ToListAsync(cancellationToken);

    public Task<PassTariff?> GetByTypeAsync(PassType type, CancellationToken cancellationToken = default) =>
        context.PassTariffs.FirstOrDefaultAsync(t => t.Type == type, cancellationToken);

    public void Add(PassTariff tariff) => context.PassTariffs.Add(tariff);
}

public sealed class StudentRepository(CekideDbContext context) : IStudentRepository
{
    public Task<Student?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        context.Students.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Student>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return idList.Count == 0
            ? []
            : await context.Students.Where(s => idList.Contains(s.Id)).ToListAsync(cancellationToken);
    }

    public Task<Student?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        context.Students.FirstOrDefaultAsync(s => s.Email == normalizedEmail, cancellationToken);

    public Task<Student?> GetByIdentityIdAsync(string identityId, CancellationToken cancellationToken = default) =>
        context.Students.FirstOrDefaultAsync(s => s.IdentityId == identityId, cancellationToken);

    public async Task<IReadOnlyList<Student>> SearchAsync(string? search, bool? active, CancellationToken cancellationToken = default)
    {
        var query = context.Students.AsQueryable();

        if (active is { } isActive)
        {
            query = query.Where(s => s.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(s => s.FullName.ToLower().Contains(term) || s.Email.Contains(term));
        }

        return await query.OrderBy(s => s.FullName).ThenBy(s => s.Id).ToListAsync(cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string normalizedEmail, int? exceptStudentId, CancellationToken cancellationToken = default) =>
        context.Students.AnyAsync(s => s.Email == normalizedEmail && s.Id != exceptStudentId, cancellationToken);

    public void Add(Student student) => context.Students.Add(student);
}
