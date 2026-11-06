using System.Text.Json.Serialization;
using CekideApi.Domain;

namespace CekideApi.Application.Recoveries;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateRecoveryReservationRequest(int CreditId, int ShiftId);

public sealed record RecoveryWindowDto(
    DateOnly Start,
    DateOnly EndExclusive,
    RecoveryWindowKind Kind);

public sealed record RecoveryCreditDto(
    int Id,
    string OriginPeriod,
    DateOnly SourceWeekStart,
    DateOnly SourceWeekEndExclusive,
    RecoveryCreditStatus Status,
    IReadOnlyList<RecoveryWindowDto> AllowedWindows,
    IReadOnlyList<string> AllowedPeriods,
    DateTime ExpiresAt,
    int? ReservationId);

public sealed record RecoveryCreditsDto(
    string TimeZone,
    DateTimeOffset AsOfAt,
    DateOnly AsOfDate,
    IReadOnlyList<RecoveryCreditDto> Credits);

public sealed record RecoveryCalendarRangeDto(DateOnly Start, DateOnly EndExclusive);

public sealed record RecoveryShiftDto(
    int Id,
    DateOnly LocalDate,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    int Capacity,
    int PlannedCount,
    int Available,
    bool Reservable,
    string? UnavailableReason);

public sealed record RecoveryAvailabilityDto(
    string TimeZone,
    DateTimeOffset AsOfAt,
    DateOnly AsOfDate,
    int CreditId,
    string OriginPeriod,
    string Period,
    IReadOnlyList<RecoveryWindowDto> AllowedWindows,
    IReadOnlyList<string> AllowedPeriods,
    IReadOnlyList<DateOnly> AllowedDates,
    RecoveryCalendarRangeDto CalendarRange,
    IReadOnlyList<RecoveryShiftDto> Shifts);

public sealed record RecoveryReservationDto(
    int Id,
    int CreditId,
    string OriginPeriod,
    int ShiftId,
    DateOnly LocalDate,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    RecoveryReservationStatus Status,
    RecoveryReservationCancelReason? CancelledReason,
    DateTime ConfirmedAt,
    DateTime? AttendedAt,
    DateTime? CancelledAt,
    bool CanCancel,
    string? StudentName);
