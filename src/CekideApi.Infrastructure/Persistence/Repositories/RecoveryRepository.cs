using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence.Repositories;

public sealed class RecoveryRepository(CekideDbContext context) : IRecoveryRepository
{
    public Task<RecoveryCredit?> GetCreditAsync(
        int studentId,
        int creditId,
        CancellationToken cancellationToken = default) =>
        context.RecoveryCredits.FirstOrDefaultAsync(
            credit => credit.StudentId == studentId && credit.Id == creditId,
            cancellationToken);

    public async Task<IReadOnlyList<RecoveryCreditData>> GetCreditsAsync(
        int studentId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from credit in context.RecoveryCredits.AsNoTracking()
            join week in context.RecoveryWeeks.AsNoTracking()
                on new { credit.StudentId, RecoveryWeekId = credit.RecoveryWeekId }
                equals new { week.StudentId, RecoveryWeekId = week.Id }
            join reservation in context.RecoveryReservations.AsNoTracking()
                    .Where(item => item.Status == RecoveryReservationStatus.Confirmed)
                on new { credit.StudentId, RecoveryCreditId = credit.Id }
                equals new { reservation.StudentId, reservation.RecoveryCreditId } into reservations
            from reservation in reservations.DefaultIfEmpty()
            where credit.StudentId == studentId
            select new { Credit = credit, Week = week, ReservationId = (int?)reservation!.Id })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
            new RecoveryCreditData(row.Credit, row.Week, row.ReservationId)).ToArray();
    }

    public async Task<IReadOnlyList<GymShift>> GetShiftsAsync(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default) =>
        await context.GymShifts.AsNoTracking()
            .Where(shift => shift.LocalDate >= fromInclusive && shift.LocalDate < toExclusive)
            .OrderBy(shift => shift.LocalDate)
            .ThenBy(shift => shift.StartsAt)
            .ToListAsync(cancellationToken);

    public Task<bool> HasAttendanceAsync(
        int studentId,
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        context.Attendances.AnyAsync(
            attendance => attendance.StudentId == studentId && attendance.LocalDate == localDate,
            cancellationToken);

    public Task<bool> HasHabitualOccupancyAsync(
        int studentId,
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        context.ShiftHabitualOccupancies.AnyAsync(occupancy =>
            occupancy.StudentId == studentId &&
            occupancy.LocalDate == localDate &&
            occupancy.Status == ShiftHabitualOccupancyStatus.Active,
            cancellationToken);

    public Task<bool> HasConfirmedReservationAsync(
        int studentId,
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        context.RecoveryReservations.AnyAsync(reservation =>
            reservation.StudentId == studentId &&
            reservation.LocalDate == localDate &&
            reservation.Status == RecoveryReservationStatus.Confirmed,
            cancellationToken);

    public async Task<IReadOnlyList<int>> GetPlannedStudentIdsAsync(
        int shiftId,
        CancellationToken cancellationToken = default)
    {
        var habitual = context.ShiftHabitualOccupancies.AsNoTracking()
            .Where(occupancy => occupancy.ShiftId == shiftId &&
                occupancy.Status == ShiftHabitualOccupancyStatus.Active)
            .Select(occupancy => occupancy.StudentId);
        var reserved = context.RecoveryReservations.AsNoTracking()
            .Where(reservation => reservation.ShiftId == shiftId &&
                reservation.Status == RecoveryReservationStatus.Confirmed)
            .Select(reservation => reservation.StudentId);
        return await habitual.Union(reserved).ToListAsync(cancellationToken);
    }

    public Task<RecoveryReservation?> GetConfirmedReservationAsync(
        int studentId,
        int creditId,
        int shiftId,
        CancellationToken cancellationToken = default) =>
        context.RecoveryReservations.FirstOrDefaultAsync(reservation =>
            reservation.StudentId == studentId &&
            reservation.RecoveryCreditId == creditId &&
            reservation.ShiftId == shiftId &&
            reservation.Status == RecoveryReservationStatus.Confirmed,
            cancellationToken);

    public Task<RecoveryReservation?> GetConfirmedReservationForCreditAsync(
        int studentId,
        int creditId,
        CancellationToken cancellationToken = default) =>
        context.RecoveryReservations.FirstOrDefaultAsync(reservation =>
            reservation.StudentId == studentId &&
            reservation.RecoveryCreditId == creditId &&
            reservation.Status == RecoveryReservationStatus.Confirmed,
            cancellationToken);

    public Task<RecoveryReservation?> GetConfirmedReservationForShiftAsync(
        int studentId,
        int shiftId,
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        context.RecoveryReservations.FirstOrDefaultAsync(reservation =>
            reservation.StudentId == studentId &&
            reservation.ShiftId == shiftId &&
            reservation.LocalDate == localDate &&
            reservation.Status == RecoveryReservationStatus.Confirmed,
            cancellationToken);

    public async Task<RecoveryMonthUsage> GetMonthUsageAsync(
        int studentId,
        int attendancePeriodId,
        string originPeriod,
        CancellationToken cancellationToken = default)
    {
        var monthAttendances = context.Attendances.AsNoTracking()
            .Where(attendance => attendance.StudentId == studentId &&
                attendance.AttendancePeriodId == attendancePeriodId);
        var ordinary = await monthAttendances.CountAsync(
            attendance => attendance.ChargeKind == AttendanceChargeKind.Ordinary,
            cancellationToken);
        var recoveryAttended = await monthAttendances.CountAsync(
            attendance => attendance.ChargeKind == AttendanceChargeKind.Recovery,
            cancellationToken);
        var recoveryUsedFromOrigin = await context.Attendances.AsNoTracking()
            .Where(attendance => attendance.StudentId == studentId &&
                attendance.ChargeKind == AttendanceChargeKind.Recovery &&
                attendance.RecoveryCreditId != null)
            .Join(context.RecoveryCredits.AsNoTracking(),
                attendance => attendance.RecoveryCreditId,
                credit => (int?)credit.Id,
                (_, credit) => credit)
            .CountAsync(credit => credit.OriginPeriod == originPeriod, cancellationToken);
        var held = await context.RecoveryCredits.AsNoTracking().CountAsync(credit =>
            credit.StudentId == studentId &&
            credit.OriginPeriod == originPeriod &&
            (credit.Status == RecoveryCreditStatus.Available ||
             credit.Status == RecoveryCreditStatus.Reserved),
            cancellationToken);
        return new RecoveryMonthUsage(ordinary, recoveryAttended, recoveryUsedFromOrigin, held);
    }

    public Task<RecoveryReservation?> GetReservationAsync(
        int studentId,
        int reservationId,
        CancellationToken cancellationToken = default) =>
        context.RecoveryReservations.FirstOrDefaultAsync(reservation =>
            reservation.StudentId == studentId && reservation.Id == reservationId,
            cancellationToken);

    public Task<int?> GetReservationShiftIdAsync(
        int studentId,
        int reservationId,
        CancellationToken cancellationToken = default) =>
        context.RecoveryReservations.AsNoTracking()
            .Where(reservation => reservation.StudentId == studentId && reservation.Id == reservationId)
            .Select(reservation => (int?)reservation.ShiftId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<RecoveryReservationData>> GetReservationsAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from reservation in context.RecoveryReservations.AsNoTracking()
            join credit in context.RecoveryCredits.AsNoTracking()
                on new { reservation.StudentId, reservation.RecoveryCreditId }
                equals new { credit.StudentId, RecoveryCreditId = credit.Id }
            join week in context.RecoveryWeeks.AsNoTracking()
                on new { credit.StudentId, RecoveryWeekId = credit.RecoveryWeekId }
                equals new { week.StudentId, RecoveryWeekId = week.Id }
            join shift in context.GymShifts.AsNoTracking() on reservation.ShiftId equals shift.Id
            where reservation.StudentId == studentId &&
                reservation.LocalDate >= fromInclusive && reservation.LocalDate < toExclusive
            orderby reservation.LocalDate, shift.StartsAt, reservation.Id
            select new { Reservation = reservation, Credit = credit, Week = week, Shift = shift })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
            new RecoveryReservationData(row.Reservation, row.Credit, row.Week, row.Shift, null)).ToArray();
    }

    public async Task<IReadOnlyList<RecoveryReservationData>> GetStudentReservationsAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from reservation in context.RecoveryReservations.AsNoTracking()
            join credit in context.RecoveryCredits.AsNoTracking()
                on new { reservation.StudentId, reservation.RecoveryCreditId }
                equals new { credit.StudentId, RecoveryCreditId = credit.Id }
            join week in context.RecoveryWeeks.AsNoTracking()
                on new { credit.StudentId, RecoveryWeekId = credit.RecoveryWeekId }
                equals new { week.StudentId, RecoveryWeekId = week.Id }
            join shift in context.GymShifts.AsNoTracking() on reservation.ShiftId equals shift.Id
            join student in context.Students.AsNoTracking() on reservation.StudentId equals student.Id
            where reservation.StudentId == studentId &&
                reservation.LocalDate >= fromInclusive && reservation.LocalDate < toExclusive
            orderby reservation.LocalDate, shift.StartsAt, reservation.Id
            select new
            {
                Reservation = reservation,
                Credit = credit,
                Week = week,
                Shift = shift,
                StudentName = student.FullName
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new RecoveryReservationData(
            row.Reservation, row.Credit, row.Week, row.Shift, row.StudentName)).ToArray();
    }

    public Task<RecoveryWeek?> GetWeekAsync(
        int studentId,
        DateOnly weekStart,
        CancellationToken cancellationToken = default) =>
        context.RecoveryWeeks.SingleOrDefaultAsync(
            week => week.StudentId == studentId && week.WeekStart == weekStart,
            cancellationToken);

    public async Task<IReadOnlyList<int>> GetStudentIdsAsync(CancellationToken cancellationToken = default) =>
        await context.Students.AsNoTracking().OrderBy(student => student.Id)
            .Select(student => student.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RecoveryWeek>> GetWeeksToCloseAsync(
        int studentId,
        DateOnly weekEndExclusive,
        CancellationToken cancellationToken = default) =>
        await context.RecoveryWeeks
            .Where(week => week.StudentId == studentId &&
                week.WeekEndExclusive <= weekEndExclusive &&
                week.ClosedAt == null)
            .OrderBy(week => week.WeekStart)
            .ToListAsync(cancellationToken);

    public async Task<RecoveryOriginBalance> GetOriginBalanceAsync(
        int studentId,
        int attendancePeriodId,
        string originPeriod,
        CancellationToken cancellationToken = default)
    {
        var ordinary = await context.Attendances.CountAsync(attendance =>
            attendance.StudentId == studentId &&
            attendance.AttendancePeriodId == attendancePeriodId &&
            attendance.ChargeKind == AttendanceChargeKind.Ordinary,
            cancellationToken);
        var usedRecovery = await context.Attendances
            .Where(attendance => attendance.StudentId == studentId &&
                attendance.ChargeKind == AttendanceChargeKind.Recovery &&
                attendance.RecoveryCreditId != null)
            .Join(context.RecoveryCredits,
                attendance => attendance.RecoveryCreditId,
                credit => (int?)credit.Id,
                (_, credit) => credit)
            .CountAsync(credit => credit.OriginPeriod == originPeriod, cancellationToken);
        var held = await context.RecoveryCredits.CountAsync(credit =>
            credit.StudentId == studentId &&
            credit.OriginPeriod == originPeriod &&
            (credit.Status == RecoveryCreditStatus.Available ||
             credit.Status == RecoveryCreditStatus.Reserved),
            cancellationToken);
        return new RecoveryOriginBalance(ordinary + usedRecovery, held);
    }

    public async Task<IReadOnlyList<RecoveryReservationState>> GetReservationsToNormalizeAsync(
        int studentId,
        DateOnly throughDate,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from reservation in context.RecoveryReservations
            join credit in context.RecoveryCredits
                on new { reservation.StudentId, reservation.RecoveryCreditId }
                equals new { credit.StudentId, RecoveryCreditId = credit.Id }
            join shift in context.GymShifts on reservation.ShiftId equals shift.Id
            where reservation.StudentId == studentId &&
                reservation.Status == RecoveryReservationStatus.Confirmed &&
                (shift.Status == GymShiftStatus.Cancelled || reservation.LocalDate <= throughDate)
            select new { Reservation = reservation, Credit = credit, Shift = shift })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
            new RecoveryReservationState(row.Reservation, row.Credit, row.Shift)).ToArray();
    }

    public async Task<IReadOnlyList<RecoveryCredit>> GetCreditsToExpireAsync(
        int studentId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await context.RecoveryCredits
            .Where(credit => credit.StudentId == studentId &&
                (credit.Status == RecoveryCreditStatus.Available ||
                 credit.Status == RecoveryCreditStatus.Reserved) &&
                credit.ExpiresAt <= now.UtcDateTime)
            .ToListAsync(cancellationToken);

    public void AddWeek(RecoveryWeek week) => context.RecoveryWeeks.Add(week);

    public void AddCredit(RecoveryCredit credit) => context.RecoveryCredits.Add(credit);

    public void AddReservation(RecoveryReservation reservation) =>
        context.RecoveryReservations.Add(reservation);
}
