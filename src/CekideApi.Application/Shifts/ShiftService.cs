using System.Globalization;
using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Shifts;

public sealed class ShiftService(IShiftRepository shifts, IClock clock)
{
    public async Task<IReadOnlyList<GymShiftDto>> ListAsync(string? period, CancellationToken cancellationToken = default)
    {
        var (from, to) = ParsePeriod(period);
        var results = await shifts.ListAsync(from, to, cancellationToken);
        return results.Select(item => Map(
            item.Shift, item.PlannedCount, item.HabitualCount, item.ReservedCount, item.ActualAttended)).ToList();
    }

    public async Task<GymShiftDto> CreateAsync(CreateShiftRequest request, string? actor, CancellationToken cancellationToken = default)
    {
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, GymCalendar.TimeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        if (request.LocalDate < today ||
            (request.LocalDate == today && request.StartsAt <= TimeOnly.FromDateTime(localNow.DateTime)))
        {
            throw new AttendanceException(400, "invalid_shift", "A shift must start in the future.");
        }

        var shift = GymShift.Create(request.LocalDate, request.StartsAt, actor, clock.UtcNow);
        var created = await shifts.CreateAsync(shift, cancellationToken);
        return Map(created, 0, 0, 0, 0);
    }

    public async Task<GymShiftDto> CancelAsync(int shiftId, string? actor, CancellationToken cancellationToken = default)
    {
        var cancelled = await shifts.CancelAsync(shiftId, RequireActor(actor), clock.UtcNow, cancellationToken);
        return Map(cancelled, 0, 0, 0, 0);
    }

    public async Task<ShiftOccupancyDto> GetOccupancyAsync(int shiftId, CancellationToken cancellationToken = default)
    {
        var data = await shifts.GetOccupancyAsync(shiftId, cancellationToken);
        var activeHabitual = data.HabitualOccupancies
            .Where(item => item.Occupancy.Status == ShiftHabitualOccupancyStatus.Active)
            .ToArray();
        var actual = data.Attendances
            .DistinctBy(item => item.Student.Id)
            .ToArray();
        var activeReservations = data.Reservations
            .Where(item => item.Reservation.Status == RecoveryReservationStatus.Confirmed)
            .ToArray();
        var plannedIds = activeHabitual.Select(item => item.Student.Id)
            .Concat(activeReservations.Select(item => item.Student.Id))
            .ToHashSet();
        var actualIds = actual.Select(item => item.Student.Id).ToHashSet();
        var attendees = activeHabitual.Select(item => item.Student)
            .Concat(activeReservations.Select(item => item.Student))
            .Concat(actual.Select(item => item.Student))
            .DistinctBy(student => student.Id)
            .Select(student => new ShiftAttendeeDto(student.Id, student.FullName))
            .ToArray();
        var plannedCount = plannedIds.Count;
        var shiftDto = Map(data.Shift, plannedCount, activeHabitual.Length, activeReservations.Length, actualIds.Count);

        return new ShiftOccupancyDto(
            shiftDto,
            plannedCount,
            activeHabitual.Length,
            activeReservations.Length,
            actualIds.Count,
            Math.Max(0, data.Shift.Capacity - plannedCount),
            data.HabitualOccupancies.Select(item => new HabitualOccupancyDto(
                item.Occupancy.Id,
                item.Student.Id,
                item.Student.FullName,
                item.Occupancy.Status,
                item.Occupancy.RegisteredBy,
                item.Occupancy.RemovedBy,
                item.Occupancy.RemovedAt)).ToArray(),
            data.Reservations.Select(item => new ShiftReservationDto(
                item.Reservation.Id,
                item.Student.Id,
                item.Student.FullName,
                item.Credit.Id,
                item.Reservation.Status,
                item.Reservation.LocalDate,
                data.Shift.StartsAt,
                data.Shift.EndsAt)).ToArray(),
            attendees);
    }

    public Task<HabitualOccupancyResult> AddHabitualAsync(
        int shiftId,
        int studentId,
        string? actor,
        CancellationToken cancellationToken = default) =>
        shifts.AddHabitualAsync(shiftId, studentId, RequireActor(actor), clock.UtcNow, cancellationToken);

    public Task<ShiftHabitualOccupancy> RemoveHabitualAsync(
        int shiftId,
        int occupancyId,
        string? actor,
        CancellationToken cancellationToken = default) =>
        shifts.RemoveHabitualAsync(shiftId, occupancyId, RequireActor(actor), clock.UtcNow, cancellationToken);

    private GymShiftDto Map(GymShift shift, int planned, int habitual, int reserved, int actual) =>
        new(shift.Id, shift.LocalDate, shift.StartsAt, shift.EndsAt, shift.Capacity, shift.Status,
            shift.IsFinished(clock.UtcNow), planned, habitual, reserved, actual);

    private static (DateOnly From, DateOnly To) ParsePeriod(string? period)
    {
        if (!DateOnly.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
        {
            throw new DomainValidationException("Period must use the YYYY-MM format.");
        }

        return (from, from.AddMonths(1));
    }

    private static string RequireActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new DomainValidationException("An authenticated staff subject is required.");
        }

        return actor;
    }
}
