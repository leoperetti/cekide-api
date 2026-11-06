using CekideApi.Application.Shifts;
using CekideApi.Domain;

namespace CekideApi.Application.Abstractions;

public interface IShiftRepository
{
    Task<IReadOnlyList<ShiftListData>> ListAsync(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default);

    Task<GymShift?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<int?> FindMatchingOpenShiftAsync(DateOnly localDate, TimeOnly localTime, CancellationToken cancellationToken = default);

    Task<GymShift> CreateAsync(GymShift shift, CancellationToken cancellationToken = default);

    Task<GymShift> CancelAsync(int id, string actor, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<ShiftOccupancyData> GetOccupancyAsync(int id, CancellationToken cancellationToken = default);

    Task<HabitualOccupancyResult> AddHabitualAsync(
        int shiftId,
        int studentId,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<ShiftHabitualOccupancy> RemoveHabitualAsync(
        int shiftId,
        int occupancyId,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
