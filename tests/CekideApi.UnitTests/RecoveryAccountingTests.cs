using CekideApi.Domain;

namespace CekideApi.UnitTests;

public sealed class RecoveryAccountingTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(7, 5)]
    public void Weekly_target_is_capped_by_the_five_recovery_weekdays(int weeklyDays, int expected)
    {
        Assert.Equal(expected, RecoveryAccounting.WeeklyTarget(weeklyDays));
    }

    [Fact]
    public void A_full_week_is_eligible_only_for_an_active_member_with_plan_limits_from_week_start()
    {
        var weekStart = new DateOnly(2026, 11, 2);

        Assert.True(RecoveryAccounting.IsWeekEligible(
            weekStart, isActive: true, membershipStartedOn: weekStart.AddDays(-7), limitsEffectiveFrom: weekStart.AddDays(-7)));
        Assert.False(RecoveryAccounting.IsWeekEligible(
            weekStart, isActive: false, membershipStartedOn: weekStart.AddDays(-7), limitsEffectiveFrom: weekStart.AddDays(-7)));
        Assert.False(RecoveryAccounting.IsWeekEligible(
            weekStart, isActive: true, membershipStartedOn: weekStart.AddDays(1), limitsEffectiveFrom: weekStart.AddDays(-7)));
        Assert.False(RecoveryAccounting.IsWeekEligible(
            weekStart, isActive: true, membershipStartedOn: weekStart.AddDays(-7), limitsEffectiveFrom: weekStart.AddDays(1)));
    }

    [Theory]
    [InlineData(4, 3, 12, 2, 1, 1, 9, 1)]
    [InlineData(4, 3, 12, 2, 0, 1, 10, 1)]
    [InlineData(4, 5, 12, 2, 0, 0, 10, 0)]
    [InlineData(4, 1, 12, 12, 0, 0, 0, 3)]
    [InlineData(4, 1, 12, 10, 4, 0, 0, 3)]
    public void Closing_a_week_grants_only_missing_days_with_uncommitted_origin_quota(
        int target,
        int attended,
        int monthlyLimit,
        int consumed,
        int held,
        int expectedGrant,
        int expectedUncommitted,
        int expectedMissing)
    {
        var result = RecoveryAccounting.CalculateClose(target, attended, monthlyLimit, consumed, held);

        Assert.Equal(expectedMissing, result.Missing);
        Assert.Equal(expectedUncommitted, result.OriginUncommitted);
        Assert.Equal(expectedGrant, result.Granted);
    }

    [Fact]
    public void Weekly_target_is_not_reduced_for_holidays()
    {
        var target = RecoveryAccounting.WeeklyTarget(4);
        var result = RecoveryAccounting.CalculateClose(
            target, attended: 3, monthlyLimit: 12, consumed: 3, held: 0);

        Assert.Equal(4, target);
        Assert.Equal(1, result.Missing);
        Assert.Equal(1, result.Granted);
    }

    [Fact]
    public void Weeks_are_closed_in_chronological_order_without_duplicate_week_starts()
    {
        var weeks = RecoveryAccounting.OrderWeeksForClosing(
        [
            new DateOnly(2026, 11, 16),
            new DateOnly(2026, 11, 2),
            new DateOnly(2026, 11, 9),
            new DateOnly(2026, 11, 9)
        ]);

        Assert.Equal(
            new[] { new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 16) },
            weeks);
    }

    [Fact]
    public void A_credit_expires_at_its_exclusive_end_without_being_extended_by_a_missed_reservation()
    {
        var lastAllowedDate = new DateOnly(2026, 12, 4);
        var endExclusive = lastAllowedDate.AddDays(1);

        Assert.False(RecoveryAccounting.IsExpired(lastAllowedDate, endExclusive));
        Assert.True(RecoveryAccounting.IsExpired(endExclusive, endExclusive));
        Assert.True(RecoveryAccounting.IsExpired(endExclusive.AddDays(1), endExclusive));
    }

    [Fact]
    public void Recovery_from_another_origin_month_is_attended_but_does_not_consume_the_destination_quota()
    {
        var summary = RecoveryAccounting.SummarizeMonth(
            monthlyLimit: 12,
            ordinaryAttended: 11,
            recoveryAttended: 2,
            recoveryUsedFromThisOrigin: 0,
            recoveryHeld: 0);

        Assert.Equal(13, summary.Attended);
        Assert.Equal(11, summary.OrdinaryAttended);
        Assert.Equal(2, summary.RecoveryAttended);
        Assert.Equal(11, summary.Consumed);
        Assert.Equal(1, summary.Remaining);
        Assert.Equal(0, summary.Exceeded);
    }

    [Fact]
    public void Credit_held_in_its_origin_reduces_the_available_balance_but_never_below_zero()
    {
        var held = RecoveryAccounting.CalculateClose(
            target: 4,
            attended: 0,
            monthlyLimit: 12,
            consumed: 10,
            held: 3);

        Assert.Equal(0, held.OriginUncommitted);
        Assert.Equal(0, held.Granted);
    }
}
