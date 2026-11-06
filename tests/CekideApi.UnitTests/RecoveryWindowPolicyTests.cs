using CekideApi.Domain;

namespace CekideApi.UnitTests;

public sealed class RecoveryWindowPolicyTests
{
    [Fact]
    public void A_regular_november_week_does_not_open_december()
    {
        var windows = RecoveryWindowPolicy.GetWindows(new DateOnly(2026, 11, 2));

        Assert.Collection(
            windows,
            window => AssertWindow(
                window,
                RecoveryWindowKind.Normal,
                new DateOnly(2026, 11, 9),
                new DateOnly(2026, 12, 1)));
        Assert.DoesNotContain(new DateOnly(2026, 12, 1), EnumerateDates(windows));
    }

    [Fact]
    public void The_last_november_week_opens_only_november_30_and_december_1_to_4()
    {
        var windows = RecoveryWindowPolicy.GetWindows(new DateOnly(2026, 11, 23));

        Assert.Collection(
            windows,
            window => AssertWindow(
                window,
                RecoveryWindowKind.Normal,
                new DateOnly(2026, 11, 30),
                new DateOnly(2026, 12, 1)),
            window => AssertWindow(
                window,
                RecoveryWindowKind.Exception,
                new DateOnly(2026, 12, 1),
                new DateOnly(2026, 12, 5)));
        Assert.Equal(
            [
                new DateOnly(2026, 11, 30),
                new DateOnly(2026, 12, 1),
                new DateOnly(2026, 12, 2),
                new DateOnly(2026, 12, 3),
                new DateOnly(2026, 12, 4)
            ],
            EnumerateDates(windows));
    }

    [Fact]
    public void A_week_starting_on_november_30_opens_only_december_7_to_11()
    {
        var windows = RecoveryWindowPolicy.GetWindows(new DateOnly(2026, 11, 30));

        Assert.Collection(
            windows,
            window => AssertWindow(
                window,
                RecoveryWindowKind.Exception,
                new DateOnly(2026, 12, 7),
                new DateOnly(2026, 12, 12)));
        Assert.Equal(
            Enumerable.Range(7, 5).Select(day => new DateOnly(2026, 12, day)),
            EnumerateDates(windows));
    }

    [Fact]
    public void A_closing_week_can_cross_the_year_without_opening_later_january_dates()
    {
        var windows = RecoveryWindowPolicy.GetWindows(new DateOnly(2026, 12, 28));

        Assert.Collection(
            windows,
            window => AssertWindow(
                window,
                RecoveryWindowKind.Exception,
                new DateOnly(2027, 1, 4),
                new DateOnly(2027, 1, 9)));
        Assert.Equal(
            Enumerable.Range(4, 5).Select(day => new DateOnly(2027, 1, day)),
            EnumerateDates(windows));
    }

    [Fact]
    public void Leap_day_is_included_and_the_exclusive_end_stays_outside_the_window()
    {
        var windows = RecoveryWindowPolicy.GetWindows(new DateOnly(2028, 2, 21));

        Assert.Collection(
            windows,
            window => AssertWindow(
                window,
                RecoveryWindowKind.Normal,
                new DateOnly(2028, 2, 28),
                new DateOnly(2028, 3, 1)),
            window => AssertWindow(
                window,
                RecoveryWindowKind.Exception,
                new DateOnly(2028, 3, 1),
                new DateOnly(2028, 3, 4)));
        Assert.Equal(
            [
                new DateOnly(2028, 2, 28),
                new DateOnly(2028, 2, 29),
                new DateOnly(2028, 3, 1),
                new DateOnly(2028, 3, 2),
                new DateOnly(2028, 3, 3)
            ],
            EnumerateDates(windows));
    }

    private static void AssertWindow(
        RecoveryWindow window,
        RecoveryWindowKind kind,
        DateOnly start,
        DateOnly endExclusive)
    {
        Assert.Equal(kind, window.Kind);
        Assert.Equal(start, window.Start);
        Assert.Equal(endExclusive, window.EndExclusive);
        Assert.True(window.Start < window.EndExclusive);
    }

    private static DateOnly[] EnumerateDates(IEnumerable<RecoveryWindow> windows) =>
        windows.SelectMany(window =>
                Enumerable.Range(0, window.EndExclusive.DayNumber - window.Start.DayNumber)
                    .Select(offset => window.Start.AddDays(offset)))
            .ToArray();
}
