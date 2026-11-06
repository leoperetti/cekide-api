using System.Data;
using CekideApi.Application.Abstractions;
using CekideApi.Application.Shifts;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CekideApi.Infrastructure.Persistence.Repositories;

public sealed class ShiftRepository(CekideDbContext context) : IShiftRepository
{
    private const int AppLockTimeoutMilliseconds = 5_000;

    public async Task<IReadOnlyList<ShiftListData>> ListAsync(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        var list = await context.GymShifts.AsNoTracking()
            .Where(shift => shift.LocalDate >= fromInclusive && shift.LocalDate < toExclusive)
            .OrderBy(shift => shift.LocalDate)
            .ThenBy(shift => shift.StartsAt)
            .ToListAsync(cancellationToken);
        var shiftIds = list.Select(shift => shift.Id).ToArray();
        var habitualCounts = await context.ShiftHabitualOccupancies.AsNoTracking()
            .Where(occupancy => shiftIds.Contains(occupancy.ShiftId) &&
                                occupancy.Status == ShiftHabitualOccupancyStatus.Active)
            .GroupBy(occupancy => occupancy.ShiftId)
            .Select(group => new
            {
                ShiftId = group.Key,
                Count = group.Select(occupancy => occupancy.StudentId).Distinct().Count()
            })
            .ToDictionaryAsync(item => item.ShiftId, item => item.Count, cancellationToken);
        var reservationCounts = await context.RecoveryReservations.AsNoTracking()
            .Where(reservation => shiftIds.Contains(reservation.ShiftId) &&
                reservation.Status == RecoveryReservationStatus.Confirmed)
            .GroupBy(reservation => reservation.ShiftId)
            .Select(group => new
            {
                ShiftId = group.Key,
                Count = group.Select(reservation => reservation.StudentId).Distinct().Count()
            })
            .ToDictionaryAsync(item => item.ShiftId, item => item.Count, cancellationToken);
        var plannedCounts = await (
                context.ShiftHabitualOccupancies.AsNoTracking()
                    .Where(occupancy => shiftIds.Contains(occupancy.ShiftId) &&
                        occupancy.Status == ShiftHabitualOccupancyStatus.Active)
                    .Select(occupancy => new { occupancy.ShiftId, occupancy.StudentId })
                    .Union(context.RecoveryReservations.AsNoTracking()
                        .Where(reservation => shiftIds.Contains(reservation.ShiftId) &&
                            reservation.Status == RecoveryReservationStatus.Confirmed)
                        .Select(reservation => new { reservation.ShiftId, reservation.StudentId })))
            .GroupBy(item => item.ShiftId)
            .Select(group => new { ShiftId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.ShiftId, item => item.Count, cancellationToken);
        var actualCounts = await context.Attendances.AsNoTracking()
            .Where(attendance => attendance.ShiftId != null && shiftIds.Contains(attendance.ShiftId.Value))
            .GroupBy(attendance => attendance.ShiftId!.Value)
            .Select(group => new
            {
                ShiftId = group.Key,
                Count = group.Select(attendance => attendance.StudentId).Distinct().Count()
            })
            .ToDictionaryAsync(item => item.ShiftId, item => item.Count, cancellationToken);
        return list.Select(shift => new ShiftListData(
            shift,
            plannedCounts.GetValueOrDefault(shift.Id),
            habitualCounts.GetValueOrDefault(shift.Id),
            reservationCounts.GetValueOrDefault(shift.Id),
            actualCounts.GetValueOrDefault(shift.Id))).ToArray();
    }

    public Task<GymShift?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        context.GymShifts.AsNoTracking().FirstOrDefaultAsync(shift => shift.Id == id, cancellationToken);

    public Task<int?> FindMatchingOpenShiftAsync(
        DateOnly localDate,
        TimeOnly localTime,
        CancellationToken cancellationToken = default) =>
        FindMatchingOpenShiftWithLockAsync(localDate, localTime, cancellationToken);

    private async Task<int?> FindMatchingOpenShiftWithLockAsync(
        DateOnly localDate,
        TimeOnly localTime,
        CancellationToken cancellationToken)
    {
        var candidates = await context.GymShifts.AsNoTracking()
            .Where(shift => shift.LocalDate == localDate &&
                            shift.Status == GymShiftStatus.Open &&
                            shift.StartsAt <= localTime &&
                            shift.EndsAt > localTime)
            .Select(shift => (int?)shift.Id)
            .ToListAsync(cancellationToken);
        foreach (var shiftId in candidates)
        {
            if (shiftId is null)
            {
                continue;
            }

            var transaction = context.Database.CurrentTransaction;
            if (transaction is not null)
            {
                await AcquireLockAsync($"cekide:shift:{shiftId.Value}", transaction, cancellationToken);
            }

            var remainsOpen = await context.GymShifts.AsNoTracking().AnyAsync(shift =>
                shift.Id == shiftId.Value &&
                shift.Status == GymShiftStatus.Open &&
                shift.LocalDate == localDate &&
                shift.StartsAt <= localTime &&
                shift.EndsAt > localTime,
                cancellationToken);
            if (remainsOpen)
            {
                return shiftId;
            }
        }

        return null;
    }

    public async Task<GymShift> CreateAsync(GymShift shift, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync($"cekide:shift-schedule:{shift.LocalDate:yyyyMMdd}", transaction, cancellationToken);

        var overlaps = await context.GymShifts.AnyAsync(candidate =>
            candidate.LocalDate == shift.LocalDate &&
            candidate.Status == GymShiftStatus.Open &&
            candidate.StartsAt < shift.EndsAt &&
            candidate.EndsAt > shift.StartsAt,
            cancellationToken);
        if (overlaps)
        {
            throw new AttendanceException(409, "shift_overlap", "The shift overlaps an existing open shift.");
        }

        context.GymShifts.Add(shift);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return shift;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new AttendanceException(409, "shift_overlap", "A shift already exists at that time.");
        }
    }

    public async Task<GymShift> CancelAsync(
        int id,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync($"cekide:shift:{id}", transaction, cancellationToken);
        var shift = await context.GymShifts.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
                    ?? throw new NotFoundException($"Shift {id} was not found.");
        shift.Cancel(actor, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return shift;
    }

    public async Task<ShiftOccupancyData> GetOccupancyAsync(int id, CancellationToken cancellationToken = default)
    {
        var shift = await context.GymShifts.AsNoTracking()
                        .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
                    ?? throw new NotFoundException($"Shift {id} was not found.");
        var occupancies = await context.ShiftHabitualOccupancies.AsNoTracking()
            .Where(occupancy => occupancy.ShiftId == id)
            .Join(context.Students.AsNoTracking(),
                occupancy => occupancy.StudentId,
                student => student.Id,
                (occupancy, student) => new { Occupancy = occupancy, Student = student })
            .OrderBy(item => item.Student.FullName)
            .Select(item => new { item.Occupancy, item.Student })
            .ToListAsync(cancellationToken);
        var attendances = await context.Attendances.AsNoTracking()
            .Where(attendance => attendance.ShiftId == id)
            .Join(context.Students.AsNoTracking(),
                attendance => attendance.StudentId,
                student => student.Id,
                (attendance, student) => new { Attendance = attendance, Student = student })
            .OrderBy(item => item.Student.FullName)
            .Select(item => new { item.Attendance, item.Student })
            .ToListAsync(cancellationToken);
        var reservations = await (
            from reservation in context.RecoveryReservations.AsNoTracking()
            join credit in context.RecoveryCredits.AsNoTracking()
                on new { reservation.StudentId, reservation.RecoveryCreditId }
                equals new { credit.StudentId, RecoveryCreditId = credit.Id }
            join student in context.Students.AsNoTracking() on reservation.StudentId equals student.Id
            where reservation.ShiftId == id
            orderby student.FullName
            select new { Reservation = reservation, Credit = credit, Student = student })
            .ToListAsync(cancellationToken);

        return new ShiftOccupancyData(
            shift,
            occupancies.Select(item => (item.Occupancy, item.Student)).ToArray(),
            reservations.Select(item => (item.Reservation, item.Credit, item.Student)).ToArray(),
            attendances.Select(item => (item.Attendance, item.Student)).ToArray());
    }

    public async Task<HabitualOccupancyResult> AddHabitualAsync(
        int shiftId,
        int studentId,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (shiftId <= 0 || studentId <= 0)
        {
            throw new DomainValidationException("Shift and student ids must be positive.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var student = await LockStudentAsync(studentId, transaction, cancellationToken);
        if (student is null)
        {
            throw new NotFoundException($"Student {studentId} was not found.");
        }

        await AcquireLockAsync($"cekide:shift:{shiftId}", transaction, cancellationToken);
        var shift = await context.GymShifts.FirstOrDefaultAsync(candidate => candidate.Id == shiftId, cancellationToken)
                    ?? throw new NotFoundException($"Shift {shiftId} was not found.");

        var existing = await context.ShiftHabitualOccupancies.FirstOrDefaultAsync(occupancy =>
            occupancy.ShiftId == shiftId &&
            occupancy.StudentId == studentId &&
            occupancy.Status == ShiftHabitualOccupancyStatus.Active,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new HabitualOccupancyResult(existing, false);
        }

        if (!student.IsActive)
        {
            throw new AttendanceException(409, "student_inactive", "An inactive student cannot be planned for a shift.");
        }

        EnsureFutureAndOpen(shift, now);

        var alreadyPlanned = await context.ShiftHabitualOccupancies.AnyAsync(occupancy =>
                occupancy.StudentId == studentId &&
                occupancy.LocalDate == shift.LocalDate &&
                occupancy.Status == ShiftHabitualOccupancyStatus.Active,
                cancellationToken) ||
            await context.RecoveryReservations.AnyAsync(reservation =>
                reservation.StudentId == studentId &&
                reservation.LocalDate == shift.LocalDate &&
                reservation.Status == RecoveryReservationStatus.Confirmed,
                cancellationToken);
        if (alreadyPlanned)
        {
            throw new AttendanceException(409, "already_planned", "The student is already planned for another shift that day.");
        }

        var alreadyAttended = await context.Attendances.AnyAsync(attendance =>
            attendance.StudentId == studentId && attendance.LocalDate == shift.LocalDate,
            cancellationToken);
        if (alreadyAttended)
        {
            throw new AttendanceException(409, "already_attended", "The student already attended that day.");
        }

        var plannedCount = await context.ShiftHabitualOccupancies
            .Where(occupancy => occupancy.ShiftId == shiftId &&
                occupancy.Status == ShiftHabitualOccupancyStatus.Active)
            .Select(occupancy => new { occupancy.ShiftId, occupancy.StudentId })
            .Union(context.RecoveryReservations
                .Where(reservation => reservation.ShiftId == shiftId &&
                    reservation.Status == RecoveryReservationStatus.Confirmed)
                .Select(reservation => new { reservation.ShiftId, reservation.StudentId }))
            .CountAsync(cancellationToken);
        if (plannedCount >= GymShift.CapacityPerShift)
        {
            throw new AttendanceException(409, "shift_full", "The shift has no planned places available.");
        }

        var occupancy = ShiftHabitualOccupancy.Create(shiftId, studentId, shift.LocalDate, actor, now);
        context.ShiftHabitualOccupancies.Add(occupancy);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HabitualOccupancyResult(occupancy, true);
    }

    public async Task<ShiftHabitualOccupancy> RemoveHabitualAsync(
        int shiftId,
        int occupancyId,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var occupancyInfo = await context.ShiftHabitualOccupancies.AsNoTracking()
            .Where(candidate => candidate.Id == occupancyId && candidate.ShiftId == shiftId)
            .Select(candidate => new { candidate.Id, candidate.StudentId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Occupancy {occupancyId} was not found.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await LockStudentAsync(occupancyInfo.StudentId, transaction, cancellationToken) is null)
        {
            throw new NotFoundException($"Student {occupancyInfo.StudentId} was not found.");
        }

        await AcquireLockAsync($"cekide:shift:{shiftId}", transaction, cancellationToken);
        var shift = await context.GymShifts.FirstOrDefaultAsync(candidate => candidate.Id == shiftId, cancellationToken)
                    ?? throw new NotFoundException($"Shift {shiftId} was not found.");
        var occupancy = await context.ShiftHabitualOccupancies.FirstOrDefaultAsync(
                            candidate => candidate.Id == occupancyId && candidate.ShiftId == shiftId,
                            cancellationToken)
                        ?? throw new NotFoundException($"Occupancy {occupancyId} was not found.");
        if (occupancy.Status == ShiftHabitualOccupancyStatus.Removed)
        {
            await transaction.CommitAsync(cancellationToken);
            return occupancy;
        }

        EnsureNotStarted(shift, now);
        occupancy.Remove(actor, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return occupancy;
    }

    private async Task<Student?> LockStudentAsync(
        int studentId,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (context.Database.IsSqlServer())
        {
            return await context.Students.FromSqlInterpolated(
                    $"SELECT * FROM students WITH (UPDLOCK, HOLDLOCK) WHERE id = {studentId}")
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await context.Students.FirstOrDefaultAsync(student => student.Id == studentId, cancellationToken);
    }

    private async Task AcquireLockAsync(
        string resource,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlServer())
        {
            return;
        }

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @timeout;
            SELECT @result;
            """;
        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);
        var timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@timeout";
        timeoutParameter.Value = AppLockTimeoutMilliseconds;
        command.Parameters.Add(timeoutParameter);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            if (result >= 0)
            {
                return;
            }

            if (result == -1 && attempt == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                continue;
            }

            throw new AttendanceException(503, "shift_lock_unavailable", "The shift schedule is busy; retry the operation.");
        }
    }

    private static void EnsureFutureAndOpen(GymShift shift, DateTimeOffset now)
    {
        if (shift.Status == GymShiftStatus.Cancelled)
        {
            throw new AttendanceException(409, "shift_unavailable", "The shift is cancelled.");
        }

        EnsureNotStarted(shift, now);
    }

    private static void EnsureNotStarted(GymShift shift, DateTimeOffset now)
    {
        if (shift.IsFinished(now) ||
            (GymCalendar.Today(now) == shift.LocalDate &&
             TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, GymCalendar.TimeZone).DateTime) >= shift.StartsAt))
        {
            throw new AttendanceException(409, "shift_unavailable", "The shift has already started.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        Microsoft.Data.SqlClient.SqlException sql => sql.Number is 2601 or 2627,
        { } inner => inner.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}
