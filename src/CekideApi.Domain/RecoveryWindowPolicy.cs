namespace CekideApi.Domain;

public enum RecoveryWindowKind
{
    Normal,
    Exception
}

public sealed record RecoveryWindow(RecoveryWindowKind Kind, DateOnly Start, DateOnly EndExclusive);

public static class RecoveryWindowPolicy
{
    public static IReadOnlyList<RecoveryWindow> GetWindows(DateOnly weekStart)
    {
        if (weekStart == default || GymCalendar.WeekStart(weekStart) != weekStart)
        {
            throw new DomainValidationException("Recovery week start must be a Monday.");
        }

        var originMonth = PaymentPeriod.From(weekStart);
        var normalStart = weekStart.AddDays(7);
        var nextMonthStart = originMonth.Next().FirstDay;
        var windows = new List<RecoveryWindow>(2);

        if (normalStart < nextMonthStart)
        {
            windows.Add(new RecoveryWindow(RecoveryWindowKind.Normal, normalStart, nextMonthStart));
        }

        var sourceFriday = weekStart.AddDays(4);
        var lastFriday = originMonth.LastDay.AddDays(-(((int)originMonth.LastDay.DayOfWeek + 2) % 7));
        if (sourceFriday >= lastFriday)
        {
            var exceptionStart = Max(normalStart, nextMonthStart);
            var exceptionEnd = Min(weekStart.AddDays(12), nextMonthStart.AddMonths(1));
            if (exceptionStart < exceptionEnd)
            {
                windows.Add(new RecoveryWindow(RecoveryWindowKind.Exception, exceptionStart, exceptionEnd));
            }
        }

        return windows;
    }

    public static bool IsAllowedDate(DateOnly weekStart, DateOnly date) =>
        GymCalendar.IsRecoveryWeekday(date) &&
        GetWindows(weekStart).Any(window => date >= window.Start && date < window.EndExclusive);

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
}
