using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence.Repositories;

public sealed class StudentInvitationRepository(CekideDbContext context) : IStudentInvitationRepository
{
    public async Task<IReadOnlyList<StudentInvitation>> ListForStudentAsync(int studentId, CancellationToken cancellationToken = default) =>
        await context.StudentInvitations.Where(i => i.StudentId == studentId).OrderBy(i => i.Id).ToListAsync(cancellationToken);

    public Task<StudentInvitation?> GetLatestByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        context.StudentInvitations
            .Where(i => i.Email == normalizedEmail)
            .OrderByDescending(i => i.SentAt)
            .ThenByDescending(i => i.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<int, InvitationStatus>> LatestStatusesAsync(
        IEnumerable<int> studentIds, CancellationToken cancellationToken = default)
    {
        var ids = studentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, InvitationStatus>();
        }

        var rows = await context.StudentInvitations
            .Where(i => ids.Contains(i.StudentId))
            .Select(i => new { i.Id, i.StudentId, i.Status })
            .ToListAsync(cancellationToken);

        return rows.GroupBy(r => r.StudentId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id).Last().Status);
    }

    public void Add(StudentInvitation invitation) => context.StudentInvitations.Add(invitation);
}
