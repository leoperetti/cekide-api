namespace CekideApi.Domain;

public readonly record struct RecoveryCloseResult(int Missing, int OriginUncommitted, int Granted);

public readonly record struct RecoveryMonthSummary(
    int Attended,
    int OrdinaryAttended,
    int RecoveryAttended,
    int Consumed,
    int Remaining,
    int Exceeded);

public static class RecoveryAccounting
{
    public static int WeeklyTarget(int weeklyDays)
    {
        if (weeklyDays <= 0)
        {
            throw new DomainValidationException("Weekly attendance limit must be positive.");
        }

        return Math.Min(weeklyDays, 5);
    }

    public static bool IsWeekEligible(
        DateOnly weekStart,
        bool isActive,
        DateOnly membershipStartedOn,
        DateOnly limitsEffectiveFrom) =>
        weekStart != default &&
        GymCalendar.WeekStart(weekStart) == weekStart &&
        isActive &&
        membershipStartedOn <= weekStart &&
        limitsEffectiveFrom <= weekStart;

    public static RecoveryCloseResult CalculateClose(
        int target,
        int attended,
        int monthlyLimit,
        int consumed,
        int held)
    {
        if (target <= 0 || attended < 0 || monthlyLimit < 0 || consumed < 0 || held < 0)
        {
            throw new DomainValidationException("Recovery accounting values cannot be negative and target must be positive.");
        }

        var missing = Math.Max(target - attended, 0);
        var originUncommitted = Math.Max(monthlyLimit - consumed - held, 0);
        return new RecoveryCloseResult(missing, originUncommitted, Math.Min(missing, originUncommitted));
    }

    public static bool IsExpired(DateOnly today, DateOnly endExclusive) =>
        endExclusive != default && today >= endExclusive;

    public static RecoveryMonthSummary SummarizeMonth(
        int monthlyLimit,
        int ordinaryAttended,
        int recoveryAttended,
        int recoveryUsedFromThisOrigin,
        int recoveryHeld)
    {
        if (monthlyLimit < 0 || ordinaryAttended < 0 || recoveryAttended < 0 ||
            recoveryUsedFromThisOrigin < 0 || recoveryHeld < 0)
        {
            throw new DomainValidationException("Recovery month totals cannot be negative.");
        }

        var consumed = ordinaryAttended + recoveryUsedFromThisOrigin;
        return new RecoveryMonthSummary(
            ordinaryAttended + recoveryAttended,
            ordinaryAttended,
            recoveryAttended,
            consumed,
            Math.Max(monthlyLimit - consumed - recoveryHeld, 0),
            Math.Max(consumed - monthlyLimit, 0));
    }

    public static IReadOnlyList<DateOnly> OrderWeeksForClosing(IEnumerable<DateOnly> weekStarts)
    {
        ArgumentNullException.ThrowIfNull(weekStarts);
        var weeks = weekStarts.Distinct().Order().ToArray();
        if (weeks.Any(week => week == default || GymCalendar.WeekStart(week) != week))
        {
            throw new DomainValidationException("Recovery weeks must start on Monday.");
        }

        return weeks;
    }
}
