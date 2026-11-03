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
}
