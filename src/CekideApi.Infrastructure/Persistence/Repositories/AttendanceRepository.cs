using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence.Repositories;

public sealed class AttendanceRepository(CekideDbContext context) : IAttendanceRepository
{
    public Task<Attendance?> GetForStudentDateAsync(
        int studentId,
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        context.Attendances.FirstOrDefaultAsync(
            attendance => attendance.StudentId == studentId && attendance.LocalDate == localDate,
            cancellationToken);

    public Task<AttendancePeriod?> GetPeriodAsync(
        int studentId,
        string period,
        CancellationToken cancellationToken = default) =>
        context.AttendancePeriods.FirstOrDefaultAsync(
            snapshot => snapshot.StudentId == studentId && snapshot.Period == period,
            cancellationToken);

    public async Task<IReadOnlyList<DateOnly>> GetAttendanceDatesAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default) =>
        await context.Attendances.AsNoTracking()
            .Where(attendance =>
                attendance.StudentId == studentId &&
                attendance.LocalDate >= fromInclusive &&
                attendance.LocalDate < toExclusive)
            .Select(attendance => attendance.LocalDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attendance>> GetAttendancesAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default) =>
        await context.Attendances.AsNoTracking()
            .Where(attendance =>
                attendance.StudentId == studentId &&
                attendance.LocalDate >= fromInclusive &&
                attendance.LocalDate < toExclusive)
            .OrderBy(attendance => attendance.LocalDate)
            .ThenBy(attendance => attendance.Id)
            .ToListAsync(cancellationToken);

    public Task<AttendanceQuotaNotification?> GetQuotaNotificationAsync(
        int studentId,
        string period,
        CancellationToken cancellationToken = default) =>
        context.AttendanceQuotaNotifications.FirstOrDefaultAsync(
            notification => notification.StudentId == studentId && notification.Period == period,
            cancellationToken);

    public void AddPeriod(AttendancePeriod period) => context.AttendancePeriods.Add(period);

    public void AddAttendance(Attendance attendance) => context.Attendances.Add(attendance);

    public void AddQuotaNotification(AttendanceQuotaNotification notification) =>
        context.AttendanceQuotaNotifications.Add(notification);
}
