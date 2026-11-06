using CekideApi.Domain;
using AttendanceEntity = CekideApi.Domain.Attendance;

namespace CekideApi.Application.Abstractions;

public interface IAttendanceRepository
{
    Task<AttendanceEntity?> GetForStudentDateAsync(
        int studentId,
        DateOnly localDate,
        CancellationToken cancellationToken = default);

    Task<AttendancePeriod?> GetPeriodAsync(
        int studentId,
        string period,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DateOnly>> GetAttendanceDatesAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttendanceEntity>> GetAttendancesAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default);

    Task<AttendanceQuotaNotification?> GetQuotaNotificationAsync(
        int studentId,
        string period,
        CancellationToken cancellationToken = default);

    void AddPeriod(AttendancePeriod period);

    void AddAttendance(AttendanceEntity attendance);

    void AddQuotaNotification(AttendanceQuotaNotification notification);
}

public interface IKioskSessionRepository
{
    Task<KioskSession?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<KioskSession?> GetByActivationCodeHashAsync(string hash, CancellationToken cancellationToken = default);

    Task<KioskSession?> GetByTokenHashAsync(string hash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KioskSession>> ListForTenantAsync(string tenant, CancellationToken cancellationToken = default);

    Task<bool> IsRevokedAsync(int id, string tenant, CancellationToken cancellationToken = default);

    void Add(KioskSession session);
}

public interface IAttendanceTransaction
{
    Task<T> ExecuteForStudentAsync<T>(
        int studentId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);
}
