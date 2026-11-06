namespace CekideApi.Domain;

public sealed class AttendancePeriod : Entity
{
    public const int MaxPlanNameLength = Plan.MaxNameLength;

    private AttendancePeriod()
    {
    }

    public int StudentId { get; private set; }

    public string Period { get; private set; } = string.Empty;

    public int PlanId { get; private set; }

    public int PlanLimitsId { get; private set; }

    public string PlanName { get; private set; } = string.Empty;

    public int WeeklyLimit { get; private set; }

    public int MonthlyLimit { get; private set; }

    public static AttendancePeriod Create(
        int studentId,
        int planId,
        int planLimitsId,
        string? planName,
        int weeklyLimit,
        int monthlyLimit,
        DateOnly localDate)
    {
        var normalizedName = planName?.Trim();
        if (studentId <= 0 || planId <= 0 || planLimitsId <= 0)
        {
            throw new DomainValidationException("Student, plan, and attendance limits ids must be positive.");
        }

        if (string.IsNullOrEmpty(normalizedName) || normalizedName.Length > MaxPlanNameLength)
        {
            throw new DomainValidationException($"Plan name is required and cannot exceed {MaxPlanNameLength} characters.");
        }

        if (weeklyLimit is < 1 or > 7 || monthlyLimit <= 0)
        {
            throw new DomainValidationException("Weekly attendance limit must be between one and seven and monthly limit must be positive.");
        }

        return new AttendancePeriod
        {
            StudentId = studentId,
            Period = PaymentPeriod.From(localDate).ToString(),
            PlanId = planId,
            PlanLimitsId = planLimitsId,
            PlanName = normalizedName,
            WeeklyLimit = weeklyLimit,
            MonthlyLimit = monthlyLimit
        };
    }
}
