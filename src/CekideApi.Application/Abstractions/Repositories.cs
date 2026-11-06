using CekideApi.Domain;

namespace CekideApi.Application.Abstractions;

public interface IPlanRepository
{
    Task<Plan?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Plan>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive name check, optionally ignoring one plan (when renaming).</summary>
    Task<bool> NameExistsAsync(string name, int? exceptPlanId, CancellationToken cancellationToken = default);

    void Add(Plan plan);

    Task<IReadOnlyList<PlanAttendanceLimits>> GetAttendanceLimitsAsync(IEnumerable<int> planIds, CancellationToken cancellationToken = default);

    void AddAttendanceLimits(PlanAttendanceLimits limits);
}

public interface IPassTariffRepository
{
    Task<IReadOnlyList<PassTariff>> ListAsync(CancellationToken cancellationToken = default);

    Task<PassTariff?> GetByTypeAsync(PassType type, CancellationToken cancellationToken = default);

    void Add(PassTariff tariff);
}

public interface IStudentRepository
{
    Task<Student?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Student>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

    Task<Student?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<Student?> GetByIdentityIdAsync(string identityId, CancellationToken cancellationToken = default);

    Task<Student?> GetByDniAsync(string normalizedDni, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Student>> SearchAsync(string? search, bool? active, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, int? exceptStudentId, CancellationToken cancellationToken = default);

    Task<bool> DniExistsAsync(string normalizedDni, int? exceptStudentId, CancellationToken cancellationToken = default);

    void Add(Student student);
}

public interface IRecoveryRepository
{
    Task<RecoveryCredit?> GetCreditAsync(
        int studentId,
        int creditId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecoveryCreditData>> GetCreditsAsync(
        int studentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GymShift>> GetShiftsAsync(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default);

    Task<bool> HasAttendanceAsync(int studentId, DateOnly localDate, CancellationToken cancellationToken = default);

    Task<bool> HasHabitualOccupancyAsync(int studentId, DateOnly localDate, CancellationToken cancellationToken = default);

    Task<bool> HasConfirmedReservationAsync(int studentId, DateOnly localDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetPlannedStudentIdsAsync(
        int shiftId,
        CancellationToken cancellationToken = default);

    Task<RecoveryReservation?> GetConfirmedReservationAsync(
        int studentId,
        int creditId,
        int shiftId,
        CancellationToken cancellationToken = default);

    Task<RecoveryReservation?> GetConfirmedReservationForCreditAsync(
        int studentId,
        int creditId,
        CancellationToken cancellationToken = default);

    Task<RecoveryReservation?> GetConfirmedReservationForShiftAsync(
        int studentId,
        int shiftId,
        DateOnly localDate,
        CancellationToken cancellationToken = default);

    Task<RecoveryMonthUsage> GetMonthUsageAsync(
        int studentId,
        int attendancePeriodId,
        string originPeriod,
        CancellationToken cancellationToken = default);

    Task<RecoveryReservation?> GetReservationAsync(
        int studentId,
        int reservationId,
        CancellationToken cancellationToken = default);

    Task<int?> GetReservationShiftIdAsync(
        int studentId,
        int reservationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecoveryReservationData>> GetReservationsAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecoveryReservationData>> GetStudentReservationsAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default);

    Task<RecoveryWeek?> GetWeekAsync(
        int studentId,
        DateOnly weekStart,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetStudentIdsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecoveryWeek>> GetWeeksToCloseAsync(
        int studentId,
        DateOnly weekEndExclusive,
        CancellationToken cancellationToken = default);

    Task<RecoveryOriginBalance> GetOriginBalanceAsync(
        int studentId,
        int attendancePeriodId,
        string originPeriod,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecoveryReservationState>> GetReservationsToNormalizeAsync(
        int studentId,
        DateOnly throughDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecoveryCredit>> GetCreditsToExpireAsync(
        int studentId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    void AddWeek(RecoveryWeek week);

    void AddCredit(RecoveryCredit credit);

    void AddReservation(RecoveryReservation reservation);
}

public readonly record struct RecoveryOriginBalance(int Consumed, int Held);

public readonly record struct RecoveryMonthUsage(
    int OrdinaryAttended,
    int RecoveryAttended,
    int RecoveryUsedFromOrigin,
    int RecoveryHeld);

public sealed record RecoveryReservationState(
    RecoveryReservation Reservation,
    RecoveryCredit Credit,
    GymShift Shift);

public sealed record RecoveryCreditData(
    RecoveryCredit Credit,
    RecoveryWeek Week,
    int? ReservationId);

public sealed record RecoveryReservationData(
    RecoveryReservation Reservation,
    RecoveryCredit Credit,
    RecoveryWeek Week,
    GymShift Shift,
    string? StudentName);
