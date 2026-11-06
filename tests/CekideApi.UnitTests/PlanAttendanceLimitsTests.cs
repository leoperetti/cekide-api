using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class PlanAttendanceLimitsTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 12)]
    [InlineData(7, 2)]
    public void Initial_configuration_is_effective_today_with_independent_integer_limits(int weekly, int monthly)
    {
        var limits = PlanAttendanceLimits.CreateInitial(1, weekly, monthly, " owner-1 ", Today);

        Assert.Equal(1, limits.PlanId);
        Assert.Equal(weekly, limits.WeeklyDays);
        Assert.Equal(monthly, limits.MonthlyDays);
        Assert.Equal("owner-1", limits.ConfiguredBy);
        Assert.Equal(Today, limits.EffectiveFrom);
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(-1, 12)]
    [InlineData(8, 12)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    public void Invalid_limits_are_rejected(int weekly, int monthly)
    {
        Assert.Throws<DomainValidationException>(() =>
            PlanAttendanceLimits.CreateInitial(1, weekly, monthly, "owner", Today));
        Assert.Throws<DomainValidationException>(() =>
            PlanAttendanceLimits.CreateRevision(1, weekly, monthly, "owner", Today));
    }

    [Theory]
    [InlineData(0, "owner")]
    [InlineData(-1, "owner")]
    [InlineData(1, null)]
    [InlineData(1, " ")]
    public void Plan_and_configuring_subject_are_required(int planId, string? subject)
    {
        Assert.Throws<DomainValidationException>(() =>
            PlanAttendanceLimits.CreateInitial(planId, 3, 12, subject, Today));
    }

    [Theory]
    [InlineData(2026, 11, 4, 2026, 12, 1)]
    [InlineData(2026, 12, 31, 2027, 1, 1)]
    [InlineData(2028, 2, 29, 2028, 3, 1)]
    public void Revisions_begin_on_the_first_day_of_the_next_month(
        int year, int month, int day, int nextYear, int nextMonth, int nextDay)
    {
        var limits = PlanAttendanceLimits.CreateRevision(1, 4, 16, "owner", new DateOnly(year, month, day));

        Assert.Equal(new DateOnly(nextYear, nextMonth, nextDay), limits.EffectiveFrom);
    }

    [Fact]
    public void A_future_revision_can_be_replaced_without_moving_its_effective_date()
    {
        var limits = PlanAttendanceLimits.CreateRevision(1, 3, 12, "owner-1", Today);

        limits.UpdatePending(4, 16, "owner-2", Today.AddDays(10));

        Assert.Equal(4, limits.WeeklyDays);
        Assert.Equal(16, limits.MonthlyDays);
        Assert.Equal("owner-2", limits.ConfiguredBy);
        Assert.Equal(new DateOnly(2026, 12, 1), limits.EffectiveFrom);
    }

    [Theory]
    [InlineData(2026, 12, 1)]
    [InlineData(2026, 12, 2)]
    public void An_effective_revision_is_immutable(int year, int month, int day)
    {
        var limits = PlanAttendanceLimits.CreateRevision(1, 3, 12, "owner-1", Today);

        Assert.Throws<ConflictException>(() =>
            limits.UpdatePending(4, 16, "owner-2", new DateOnly(year, month, day)));
        Assert.Equal(3, limits.WeeklyDays);
        Assert.Equal(12, limits.MonthlyDays);
        Assert.Equal("owner-1", limits.ConfiguredBy);
    }

    [Fact]
    public void Initial_configuration_cannot_be_overwritten_on_its_creation_day()
    {
        var limits = PlanAttendanceLimits.CreateInitial(1, 3, 12, "owner-1", Today);

        Assert.Throws<ConflictException>(() => limits.UpdatePending(4, 16, "owner-2", Today));
    }

    [Fact]
    public void Invalid_pending_update_leaves_the_revision_unchanged()
    {
        var limits = PlanAttendanceLimits.CreateRevision(1, 3, 12, "owner-1", Today);

        Assert.Throws<DomainValidationException>(() => limits.UpdatePending(8, 16, "owner-2", Today));
        Assert.Equal(3, limits.WeeklyDays);
        Assert.Equal(12, limits.MonthlyDays);
        Assert.Equal("owner-1", limits.ConfiguredBy);
    }
}
