namespace CekideApi.Domain;

public enum RecoveryWeekEligibility
{
    Eligible,
    Excluded
}

public sealed class RecoveryWeek : Entity
{
    private RecoveryWeek()
    {
    }

    public int StudentId { get; private set; }

    public DateOnly WeekStart { get; private set; }

    public DateOnly WeekEndExclusive { get; private set; }

    public string OriginPeriod { get; private set; } = string.Empty;

    public int AttendancePeriodId { get; private set; }

    public int PlanLimitsId { get; private set; }

    public int WeeklyTarget { get; private set; }

    public RecoveryWeekEligibility Eligibility { get; private set; }

    public DateTime? ClosedAt { get; private set; }

    public int? AttendedCount { get; private set; }

    public int? GrantedCount { get; private set; }

    public static RecoveryWeek Create(
        int studentId,
        DateOnly weekStart,
        int attendancePeriodId,
        int planLimitsId,
        int weeklyDays,
        RecoveryWeekEligibility eligibility)
    {
        if (studentId <= 0 || attendancePeriodId <= 0 || planLimitsId <= 0)
        {
            throw new DomainValidationException("Student, attendance period, and plan limits ids must be positive.");
        }

        if (weekStart == default || GymCalendar.WeekStart(weekStart) != weekStart)
        {
            throw new DomainValidationException("Recovery week start must be a Monday.");
        }

        if (weeklyDays is < 1 or > 7 || !Enum.IsDefined(eligibility))
        {
            throw new DomainValidationException("Recovery weekly limits and eligibility must be valid.");
        }

        return new RecoveryWeek
        {
            StudentId = studentId,
            WeekStart = weekStart,
            WeekEndExclusive = weekStart.AddDays(7),
            OriginPeriod = PaymentPeriod.From(weekStart).ToString(),
            AttendancePeriodId = attendancePeriodId,
            PlanLimitsId = planLimitsId,
            WeeklyTarget = RecoveryAccounting.WeeklyTarget(weeklyDays),
            Eligibility = eligibility
        };
    }

    public void Close(DateTimeOffset now, int attendedCount, int grantedCount)
    {
        if (ClosedAt is not null)
        {
            throw new ConflictException("Recovery week is already closed.");
        }

        if (now == default || now.ToUniversalTime() < GymCalendar.StartOfDayUtc(WeekEndExclusive))
        {
            throw new ConflictException("Recovery week cannot close before its local end.");
        }

        if (attendedCount < 0 || grantedCount < 0 || grantedCount > WeeklyTarget ||
            Eligibility == RecoveryWeekEligibility.Excluded && grantedCount != 0)
        {
            throw new DomainValidationException("Recovery week close counts are invalid.");
        }

        AttendedCount = attendedCount;
        GrantedCount = grantedCount;
        ClosedAt = now.UtcDateTime;
    }
}
