namespace CekideApi.Domain;

public enum AttendanceSource
{
    Self,
    Kiosk
}

public enum AttendanceChargeKind
{
    Ordinary,
    Recovery
}

public sealed class Attendance : Entity
{
    private Attendance()
    {
    }

    public int StudentId { get; private set; }

    public int AttendancePeriodId { get; private set; }

    public DateOnly LocalDate { get; private set; }

    public DateTime CheckedInAt { get; private set; }

    public AttendanceSource Source { get; private set; }

    public int? KioskSessionId { get; private set; }

    public int? ShiftId { get; private set; }

    public bool WeeklyExceeded { get; private set; }

    public bool MonthlyExceeded { get; private set; }

    public bool MonthlyExhausted { get; private set; }

    public AttendanceChargeKind ChargeKind { get; private set; } = AttendanceChargeKind.Ordinary;

    public int? RecoveryCreditId { get; private set; }

    public static Attendance Create(
        int studentId,
        int attendancePeriodId,
        DateOnly localDate,
        DateTimeOffset checkedInAt,
        AttendanceSource source,
        int? kioskSessionId,
        AttendanceQuotaSnapshot quota,
        int? shiftId = null,
        AttendanceChargeKind chargeKind = AttendanceChargeKind.Ordinary,
        int? recoveryCreditId = null)
    {
        ArgumentNullException.ThrowIfNull(quota);

        if (studentId <= 0 || attendancePeriodId <= 0 || shiftId is <= 0 || recoveryCreditId is <= 0 ||
            !Enum.IsDefined(chargeKind) ||
            chargeKind == AttendanceChargeKind.Recovery && recoveryCreditId is null ||
            chargeKind == AttendanceChargeKind.Ordinary && recoveryCreditId is not null)
        {
            throw new DomainValidationException("Attendance charge, student, period, and optional ids must be valid.");
        }

        if (!Enum.IsDefined(source))
        {
            throw new DomainValidationException("Attendance source is invalid.");
        }

        if (source == AttendanceSource.Kiosk && kioskSessionId is not > 0 ||
            source == AttendanceSource.Self && kioskSessionId is not null)
        {
            throw new DomainValidationException("Kiosk session id must be present only for kiosk attendance.");
        }

        var utcCheckedInAt = checkedInAt.ToUniversalTime();
        if (checkedInAt == default || GymCalendar.Today(utcCheckedInAt) != localDate)
        {
            throw new DomainValidationException("Attendance local date must match the captured server timestamp.");
        }

        return new Attendance
        {
            StudentId = studentId,
            AttendancePeriodId = attendancePeriodId,
            LocalDate = localDate,
            CheckedInAt = utcCheckedInAt.UtcDateTime,
            Source = source,
            KioskSessionId = kioskSessionId,
            ShiftId = shiftId,
            ChargeKind = chargeKind,
            RecoveryCreditId = recoveryCreditId,
            WeeklyExceeded = quota.WeeklyExceeded,
            MonthlyExceeded = quota.MonthlyExceeded,
            MonthlyExhausted = quota.MonthlyExhausted
        };
    }
}
