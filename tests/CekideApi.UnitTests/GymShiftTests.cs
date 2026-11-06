using CekideApi.Domain;

namespace CekideApi.UnitTests;

public sealed class GymShiftTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creates_a_one_hour_weekday_shift_with_fixed_capacity()
    {
        var shift = GymShift.Create(Monday, new TimeOnly(18, 0), "teacher-1", Now);

        Assert.Equal(Monday, shift.LocalDate);
        Assert.Equal(new TimeOnly(18, 0), shift.StartsAt);
        Assert.Equal(new TimeOnly(19, 0), shift.EndsAt);
        Assert.Equal(12, shift.Capacity);
        Assert.Equal(GymShiftStatus.Open, shift.Status);
        Assert.False(shift.IsFinished(Now));
    }

    [Theory]
    [InlineData("2026-11-07", "18:00")]
    [InlineData("2026-11-08", "18:00")]
    [InlineData("2026-11-02", "23:30")]
    public void Rejects_weekends_and_shifts_ending_outside_the_local_day(string date, string start)
    {
        Assert.Throws<DomainValidationException>(() =>
            GymShift.Create(DateOnly.Parse(date), TimeOnly.Parse(start), "teacher-1", Now));
    }

    [Fact]
    public void Allows_a_minute_aligned_start_time()
    {
        var shift = GymShift.Create(Monday, new TimeOnly(18, 30), "teacher-1", Now);

        Assert.Equal(new TimeOnly(19, 30), shift.EndsAt);
    }

    [Fact]
    public void A_cancelled_shift_keeps_its_audit_and_is_idempotent()
    {
        var shift = GymShift.Create(Monday, new TimeOnly(18, 0), "teacher-1", Now);

        shift.Cancel("owner-1", Now.AddMinutes(5));
        var cancelledAt = shift.CancelledAt;
        shift.Cancel("teacher-1", Now.AddMinutes(10));

        Assert.Equal(GymShiftStatus.Cancelled, shift.Status);
        Assert.Equal("owner-1", shift.CancelledBy);
        Assert.Equal(Now.AddMinutes(5).UtcDateTime, cancelledAt);
        Assert.Equal(cancelledAt, shift.CancelledAt);
    }

    [Fact]
    public void Finished_is_derived_from_the_clock_without_changing_the_open_status()
    {
        var shift = GymShift.Create(Monday, new TimeOnly(18, 0), "teacher-1", Now);

        Assert.True(shift.IsFinished(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.Zero)));
        Assert.Equal(GymShiftStatus.Open, shift.Status);
    }

    [Fact]
    public void Habitual_occupancy_can_be_removed_once_without_erasing_its_audit()
    {
        var occupancy = ShiftHabitualOccupancy.Create(7, 42, Monday, "teacher-1", Now);

        occupancy.Remove("owner-1", Now.AddMinutes(5));
        var removedAt = occupancy.RemovedAt;
        occupancy.Remove("teacher-1", Now.AddMinutes(10));

        Assert.Equal(ShiftHabitualOccupancyStatus.Removed, occupancy.Status);
        Assert.Equal("owner-1", occupancy.RemovedBy);
        Assert.Equal(Now.AddMinutes(5).UtcDateTime, removedAt);
        Assert.Equal(removedAt, occupancy.RemovedAt);
    }
}
