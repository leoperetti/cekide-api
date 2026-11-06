using CekideApi.Domain;
using AttendanceEntity = CekideApi.Domain.Attendance;

namespace CekideApi.Application.Shifts;

public sealed record CreateShiftRequest(DateOnly LocalDate, TimeOnly StartsAt);

public sealed record AddHabitualOccupancyRequest(int StudentId);

public sealed record GymShiftDto(
    int Id,
    DateOnly LocalDate,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    int Capacity,
    GymShiftStatus Status,
    bool IsFinished,
    int PlannedCount,
    int HabitualCount,
    int ReservedCount,
    int ActualAttended);

public sealed record HabitualOccupancyDto(
    int Id,
    int StudentId,
    string StudentName,
    ShiftHabitualOccupancyStatus Status,
    string RegisteredBy,
    string? RemovedBy,
    DateTime? RemovedAt);

public sealed record ShiftAttendeeDto(int StudentId, string StudentName);

public sealed record ShiftReservationDto(
    int Id,
    int StudentId,
    string StudentName,
    int CreditId,
    RecoveryReservationStatus Status,
    DateOnly LocalDate,
    TimeOnly StartsAt,
    TimeOnly EndsAt);

public sealed record ShiftOccupancyDto(
    GymShiftDto Shift,
    int PlannedCount,
    int HabitualCount,
    int ReservedCount,
    int ActualAttended,
    int Available,
    IReadOnlyList<HabitualOccupancyDto> HabitualOccupancies,
    IReadOnlyList<ShiftReservationDto> Reservations,
    IReadOnlyList<ShiftAttendeeDto> Attendees);

public sealed record ShiftOccupancyData(
    GymShift Shift,
    IReadOnlyList<(ShiftHabitualOccupancy Occupancy, Student Student)> HabitualOccupancies,
    IReadOnlyList<(RecoveryReservation Reservation, RecoveryCredit Credit, Student Student)> Reservations,
    IReadOnlyList<(AttendanceEntity Attendance, Student Student)> Attendances);

public sealed record HabitualOccupancyResult(ShiftHabitualOccupancy Occupancy, bool Created);

public sealed record ShiftListData(GymShift Shift, int PlannedCount, int HabitualCount, int ReservedCount, int ActualAttended);
