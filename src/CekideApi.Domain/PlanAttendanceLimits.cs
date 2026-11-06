namespace CekideApi.Domain;

public sealed class PlanAttendanceLimits : Entity
{
    public const int MaxConfiguredByLength = 100;

    private PlanAttendanceLimits()
    {
    }

    public int PlanId { get; private set; }

    public int WeeklyDays { get; private set; }

    public int MonthlyDays { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public string ConfiguredBy { get; private set; } = string.Empty;

    public static PlanAttendanceLimits CreateInitial(
        int planId, int weeklyDays, int monthlyDays, string? configuredBy, DateOnly today) =>
        Create(planId, weeklyDays, monthlyDays, configuredBy, today);

    public static PlanAttendanceLimits CreateRevision(
        int planId, int weeklyDays, int monthlyDays, string? configuredBy, DateOnly today) =>
        Create(planId, weeklyDays, monthlyDays, configuredBy, GymCalendar.MonthEndExclusive(today));

    public void UpdatePending(int weeklyDays, int monthlyDays, string? configuredBy, DateOnly today)
    {
        if (EffectiveFrom <= today)
        {
            throw new ConflictException("Effective attendance limits cannot be changed; create a future revision.");
        }

        SetLimits(weeklyDays, monthlyDays, configuredBy);
    }

    private static PlanAttendanceLimits Create(
        int planId, int weeklyDays, int monthlyDays, string? configuredBy, DateOnly effectiveFrom)
    {
        if (planId <= 0)
        {
            throw new DomainValidationException("Plan id must be positive.");
        }

        var limits = new PlanAttendanceLimits { PlanId = planId, EffectiveFrom = effectiveFrom };
        limits.SetLimits(weeklyDays, monthlyDays, configuredBy);
        return limits;
    }

    private void SetLimits(int weeklyDays, int monthlyDays, string? configuredBy)
    {
        if (weeklyDays is < 1 or > 7 || monthlyDays <= 0)
        {
            throw new DomainValidationException("Weekly days must be between one and seven and monthly days must be positive.");
        }

        var subject = configuredBy?.Trim();
        if (string.IsNullOrEmpty(subject) || subject.Length > MaxConfiguredByLength)
        {
            throw new DomainValidationException($"Configuring subject is required and cannot exceed {MaxConfiguredByLength} characters.");
        }

        WeeklyDays = weeklyDays;
        MonthlyDays = monthlyDays;
        ConfiguredBy = subject;
    }
}
