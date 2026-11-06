using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class GymCalendarTests
{
    [Theory]
    [InlineData("2026-11-11T02:59:59Z", "2026-11-10")]
    [InlineData("2026-11-11T03:00:00Z", "2026-11-11")]
    [InlineData("2026-12-01T02:59:59Z", "2026-11-30")]
    [InlineData("2026-12-01T03:00:00Z", "2026-12-01")]
    [InlineData("2027-01-01T01:00:00Z", "2026-12-31")]
    [InlineData("2026-07-15T12:00:00Z", "2026-07-15")]
    public void Today_is_the_calendar_date_in_buenos_aires(string utc, string expectedLocalDate)
    {
        var today = GymCalendar.Today(DateTimeOffset.Parse(utc));

        Assert.Equal(DateOnly.Parse(expectedLocalDate), today);
    }

    [Fact]
    public void The_time_zone_is_America_Argentina_Buenos_Aires()
    {
        Assert.Equal("America/Argentina/Buenos_Aires", GymCalendar.TimeZoneId);
        Assert.Equal(TimeSpan.FromHours(-3), GymCalendar.TimeZone.GetUtcOffset(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData("2026-11-30", "2026-11-30", "2026-12-07")]
    [InlineData("2026-12-06", "2026-11-30", "2026-12-07")]
    [InlineData("2027-01-01", "2026-12-28", "2027-01-04")]
    [InlineData("2028-02-29", "2028-02-28", "2028-03-06")]
    public void Weeks_are_monday_to_monday_with_exclusive_end(string date, string start, string end)
    {
        var day = DateOnly.Parse(date);
        Assert.Equal(DateOnly.Parse(start), GymCalendar.WeekStart(day));
        Assert.Equal(DateOnly.Parse(end), GymCalendar.WeekEndExclusive(day));
    }

    [Theory]
    [InlineData("2026-12-31", "2026-12-01", "2027-01-01")]
    [InlineData("2028-02-29", "2028-02-01", "2028-03-01")]
    public void Months_use_calendar_boundaries(string date, string start, string end)
    {
        var day = DateOnly.Parse(date);
        Assert.Equal(DateOnly.Parse(start), GymCalendar.MonthStart(day));
        Assert.Equal(DateOnly.Parse(end), GymCalendar.MonthEndExclusive(day));
    }

    [Theory]
    [InlineData("2026-11-02", true)]
    [InlineData("2026-11-06", true)]
    [InlineData("2026-11-07", false)]
    [InlineData("2026-11-08", false)]
    public void Recovery_weekdays_do_not_restrict_check_in_dates(string date, bool expected)
    {
        Assert.Equal(expected, GymCalendar.IsRecoveryWeekday(DateOnly.Parse(date)));
    }

    [Fact]
    public void Local_midnight_is_converted_using_the_gym_time_zone()
    {
        Assert.Equal(DateTimeOffset.Parse("2028-02-29T03:00:00Z"),
            GymCalendar.StartOfDayUtc(new DateOnly(2028, 2, 29)));
    }
}
