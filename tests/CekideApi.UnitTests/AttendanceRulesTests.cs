using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class AttendanceRulesTests
{
    [Fact]
    public void The_fourth_weekly_attendance_is_counted_even_when_the_weekly_limit_is_exceeded()
    {
        var dates = new[]
        {
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 3),
            new DateOnly(2026, 11, 4)
        };

        var result = AttendanceRules.EvaluateAfterCheckIn(
            new DateOnly(2026, 11, 5), dates, weeklyLimit: 3, monthlyLimit: 12);

        Assert.Equal(4, result.WeeklyConsumed);
        Assert.Equal(4, result.MonthlyConsumed);
        Assert.True(result.WeeklyExceeded);
        Assert.False(result.MonthlyExceeded);
        Assert.False(result.MonthlyExhausted);
    }

    [Fact]
    public void A_week_spans_month_boundaries_but_monthly_consumption_does_not()
    {
        var dates = new[]
        {
            new DateOnly(2026, 10, 27),
            new DateOnly(2026, 10, 29),
            new DateOnly(2026, 10, 31)
        };

        var result = AttendanceRules.EvaluateAfterCheckIn(
            new DateOnly(2026, 11, 1), dates, weeklyLimit: 3, monthlyLimit: 12);

        Assert.Equal(4, result.WeeklyConsumed);
        Assert.Equal(1, result.MonthlyConsumed);
        Assert.True(result.WeeklyExceeded);
        Assert.False(result.MonthlyExceeded);
    }

    [Fact]
    public void A_new_week_starts_with_a_fresh_weekly_count()
    {
        var dates = new[]
        {
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 3),
            new DateOnly(2026, 11, 4)
        };

        var result = AttendanceRules.EvaluateAfterCheckIn(
            new DateOnly(2026, 11, 9), dates, weeklyLimit: 3, monthlyLimit: 12);

        Assert.Equal(1, result.WeeklyConsumed);
        Assert.Equal(4, result.MonthlyConsumed);
        Assert.False(result.WeeklyExceeded);
    }

    [Fact]
    public void An_existing_local_date_is_rejected_as_a_duplicate()
    {
        var localDate = new DateOnly(2026, 11, 5);

        Assert.Throws<ConflictException>(() => AttendanceRules.EvaluateAfterCheckIn(
            localDate, [localDate], weeklyLimit: 3, monthlyLimit: 12));
    }

    [Fact]
    public void The_twelfth_monthly_attendance_exhausts_the_quota_but_is_not_over_the_limit()
    {
        var dates = Enumerable.Range(1, 11)
            .Select(day => new DateOnly(2026, 11, day))
            .ToArray();

        var result = AttendanceRules.EvaluateAfterCheckIn(
            new DateOnly(2026, 11, 12), dates, weeklyLimit: 7, monthlyLimit: 12);

        Assert.Equal(12, result.MonthlyConsumed);
        Assert.False(result.MonthlyExceeded);
        Assert.True(result.MonthlyExhausted);
    }

    [Fact]
    public void The_thirteenth_monthly_attendance_is_allowed_and_exceeds_the_quota_by_one()
    {
        var dates = Enumerable.Range(1, 12)
            .Select(day => new DateOnly(2026, 11, day))
            .ToArray();

        var result = AttendanceRules.EvaluateAfterCheckIn(
            new DateOnly(2026, 11, 13), dates, weeklyLimit: 7, monthlyLimit: 12);

        Assert.Equal(13, result.MonthlyConsumed);
        Assert.True(result.MonthlyExceeded);
        Assert.True(result.MonthlyExhausted);
    }
}
