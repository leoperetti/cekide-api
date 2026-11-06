using CekideApi.Domain;
using AttendanceEntity = CekideApi.Domain.Attendance;
using System.Text.Json.Serialization;

namespace CekideApi.Application.Attendance;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttendanceCheckInRequest(string? Dni);

public enum AttendanceCheckInOutcome
{
    Created,
    AlreadyCheckedIn
}

public sealed record AttendanceDto(
    int Id,
    DateOnly LocalDate,
    DateTime CheckedInAt,
    AttendanceSource Source,
    AttendanceChargeKind ChargeKind,
    int? ShiftId,
    int? RecoveryCreditId,
    string? RecoveryOriginPeriod,
    bool WeeklyExceeded,
    bool MonthlyExceeded,
    bool MonthlyExhausted)
{
    public static AttendanceDto From(AttendanceEntity attendance, string? recoveryOriginPeriod = null) =>
        new(attendance.Id, attendance.LocalDate, DateTime.SpecifyKind(attendance.CheckedInAt, DateTimeKind.Utc),
            attendance.Source, attendance.ChargeKind, attendance.ShiftId, attendance.RecoveryCreditId,
            recoveryOriginPeriod, attendance.WeeklyExceeded, attendance.MonthlyExceeded, attendance.MonthlyExhausted);
}

public sealed record WeekAttendanceSummaryDto(
    DateOnly Start,
    DateOnly End,
    int Limit,
    int Consumed,
    int Remaining,
    int Exceeded);

public sealed record MonthAttendanceSummaryDto(
    int Limit,
    int Consumed,
    int Remaining,
    int Exceeded,
    int Attended,
    int OrdinaryAttended,
    int RecoveryAttended,
    int RecoveryHeld);

public sealed record AttendanceSummaryDto(
    string TimeZone,
    DateOnly AsOfDate,
    string Period,
    string PlanName,
    WeekAttendanceSummaryDto Week,
    MonthAttendanceSummaryDto Month);

public sealed record AttendanceCalendarPeriodSummaryDto(
    string? PlanName,
    int? WeeklyLimit,
    int? Limit,
    int Consumed,
    int? Remaining,
    int? Exceeded,
    int Attended,
    int OrdinaryAttended,
    int RecoveryAttended,
    int RecoveryHeld);

public sealed record AttendanceCalendarDto(
    string TimeZone,
    DateOnly AsOfDate,
    string Period,
    AttendanceCalendarPeriodSummaryDto PeriodSummary,
    AttendanceSummaryDto? CurrentSummary,
    IReadOnlyList<AttendanceDto> Attendances);

public sealed record AttendanceCheckInResultDto(
    AttendanceCheckInOutcome Outcome,
    AttendanceDto Attendance,
    AttendanceSummaryDto Summary,
    IReadOnlyList<string> Warnings,
    AttendanceQuotaNotificationStatus? QuotaNoticeStatus);
