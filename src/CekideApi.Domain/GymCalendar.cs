namespace CekideApi.Domain;

/// <summary>Business calendar of the gym: every "day 10" decision uses Buenos Aires local dates.</summary>
public static class GymCalendar
{
    public const string TimeZoneId = "America/Argentina/Buenos_Aires";

    public static TimeZoneInfo TimeZone { get; } = Resolve();

    public static DateOnly Today(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, TimeZone).DateTime);

    public static DateOnly WeekStart(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static DateOnly WeekEndExclusive(DateOnly date) => WeekStart(date).AddDays(7);

    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    public static DateOnly MonthEndExclusive(DateOnly date) => MonthStart(date).AddMonths(1);

    public static bool IsRecoveryWeekday(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static DateTimeOffset StartOfDayUtc(DateOnly date) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), TimeZone));

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows hosts without ICU data only know the legacy id.
            return TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
        }
    }
}
