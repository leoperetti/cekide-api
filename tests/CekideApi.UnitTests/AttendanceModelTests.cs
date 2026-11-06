using CekideApi.Domain;
using CekideApi.Infrastructure.Persistence;
using CekideApi.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.UnitTests;

public class AttendanceModelTests
{
    [Fact]
    public void Period_freezes_the_plan_and_limits_for_the_attendance_month()
    {
        var period = AttendancePeriod.Create(
            studentId: 7,
            planId: 3,
            planLimitsId: 11,
            planName: "  Three days  ",
            weeklyLimit: 3,
            monthlyLimit: 12,
            localDate: new DateOnly(2026, 11, 5));

        Assert.Equal(7, period.StudentId);
        Assert.Equal("2026-11", period.Period);
        Assert.Equal(3, period.PlanId);
        Assert.Equal(11, period.PlanLimitsId);
        Assert.Equal("Three days", period.PlanName);
        Assert.Equal(3, period.WeeklyLimit);
        Assert.Equal(12, period.MonthlyLimit);
    }

    [Fact]
    public void Attendance_keeps_the_server_time_and_local_date()
    {
        var checkedInAt = DateTimeOffset.Parse("2026-11-05T02:59:00Z");
        var attendance = Attendance.Create(
            studentId: 7,
            attendancePeriodId: 4,
            localDate: new DateOnly(2026, 11, 4),
            checkedInAt: checkedInAt,
            source: AttendanceSource.Self,
            kioskSessionId: null,
            quota: new AttendanceQuotaSnapshot(4, 12, true, false, true));

        Assert.Equal(new DateOnly(2026, 11, 4), attendance.LocalDate);
        Assert.Equal(DateTimeKind.Utc, attendance.CheckedInAt.Kind);
        Assert.Equal(checkedInAt.UtcDateTime, attendance.CheckedInAt);
        Assert.Equal(AttendanceSource.Self, attendance.Source);
        Assert.Null(attendance.KioskSessionId);
        Assert.True(attendance.WeeklyExceeded);
        Assert.False(attendance.MonthlyExceeded);
        Assert.True(attendance.MonthlyExhausted);
        Assert.Equal(AttendanceChargeKind.Ordinary, attendance.ChargeKind);
    }

    [Fact]
    public void Kiosk_attendance_requires_a_kiosk_session()
    {
        var timestamp = DateTimeOffset.Parse("2026-11-05T12:00:00Z");
        var quota = new AttendanceQuotaSnapshot(1, 1, false, false, false);

        Assert.Throws<DomainValidationException>(() => Attendance.Create(
            7, 4, GymCalendar.Today(timestamp), timestamp,
            AttendanceSource.Kiosk, null, quota));
    }

    [Fact]
    public void Persistence_uses_contract_values_for_attendance_enums()
    {
        var options = new DbContextOptionsBuilder<CekideDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=cekide_model_test")
            .Options;
        using var context = new CekideDbContext(options, new SystemClock());
        var attendance = context.Model.FindEntityType(typeof(Attendance))!;
        var sourceConverter = attendance.FindProperty(nameof(Attendance.Source))!.GetValueConverter()!;
        var chargeConverter = attendance.FindProperty(nameof(Attendance.ChargeKind))!.GetValueConverter()!;

        Assert.Equal("kiosk", sourceConverter.ConvertToProvider(AttendanceSource.Kiosk));
        Assert.Equal("self", sourceConverter.ConvertToProvider(AttendanceSource.Self));
        Assert.Equal("ordinary", chargeConverter.ConvertToProvider(AttendanceChargeKind.Ordinary));

        var notification = context.Model.FindEntityType(typeof(AttendanceQuotaNotification))!;
        var statusConverter = notification.FindProperty(nameof(AttendanceQuotaNotification.Status))!.GetValueConverter()!;
        Assert.Equal(
            "delivery_unknown",
            statusConverter.ConvertToProvider(AttendanceQuotaNotificationStatus.DeliveryUnknown));
    }
}
