namespace CekideApi.Domain;

/// <summary>Business calendar of the gym: every "day 10" decision uses Buenos Aires local dates.</summary>
public static class GymCalendar
{
    public const string TimeZoneId = "America/Argentina/Buenos_Aires";

    public static TimeZoneInfo TimeZone { get; } = Resolve();

    public static DateOnly Today(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, TimeZone).DateTime);

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
