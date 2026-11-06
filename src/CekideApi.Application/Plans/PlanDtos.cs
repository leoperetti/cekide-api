using CekideApi.Domain;

namespace CekideApi.Application.Plans;

public abstract record PlanLimitsRequest
{
    private int? _weeklyDays;
    private int? _monthlyDays;

    public int? WeeklyDays
    {
        get => _weeklyDays;
        init { _weeklyDays = value; HasWeeklyDays = true; }
    }

    public int? MonthlyDays
    {
        get => _monthlyDays;
        init { _monthlyDays = value; HasMonthlyDays = true; }
    }

    internal bool HasWeeklyDays { get; private set; }

    internal bool HasMonthlyDays { get; private set; }

    public (int Weekly, int Monthly)? ReadLimits()
    {
        if (!HasWeeklyDays && !HasMonthlyDays)
        {
            return null;
        }

        if (WeeklyDays is not { } weekly || MonthlyDays is not { } monthly ||
            weekly is < 1 or > 7 || monthly <= 0)
        {
            throw new DomainValidationException("Supply both integer limits: weekly days between one and seven and positive monthly days.");
        }

        return (weekly, monthly);
    }
}

public sealed record CreatePlanRequest(string? Name, decimal? MonthlyPrice) : PlanLimitsRequest;

public sealed record UpdatePlanRequest(string? Name, decimal? MonthlyPrice, bool? IsActive) : PlanLimitsRequest;

public sealed record PassTariffRequest(PassType? Type, decimal? Price);

public sealed record PendingAttendanceLimitsDto(int WeeklyDays, int MonthlyDays, DateOnly EffectiveFrom);

public sealed record PlanDto(int Id, string Name, decimal MonthlyPrice, bool IsActive,
    int? WeeklyDays = null, int? MonthlyDays = null, DateOnly? AttendanceLimitsEffectiveFrom = null,
    PendingAttendanceLimitsDto? PendingAttendanceLimits = null)
{
    public static PlanDto From(Plan plan, PlanAttendanceLimits? current = null, PlanAttendanceLimits? pending = null) =>
        new(plan.Id, plan.Name, plan.MonthlyPrice, plan.IsActive, current?.WeeklyDays, current?.MonthlyDays,
            current?.EffectiveFrom, pending is null ? null :
                new PendingAttendanceLimitsDto(pending.WeeklyDays, pending.MonthlyDays, pending.EffectiveFrom));
}

public sealed record PassTariffDto(PassType Type, decimal Price)
{
    public static PassTariffDto From(PassTariff tariff) => new(tariff.Type, tariff.Price);
}
