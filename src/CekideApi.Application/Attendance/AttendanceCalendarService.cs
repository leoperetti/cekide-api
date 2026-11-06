using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Attendance;

public sealed class AttendanceCalendarService(
    IStudentRepository students,
    IPlanRepository plans,
    IAttendanceRepository attendances,
    IRecoveryRepository recoveries,
    IClock clock)
{
    public Task<AttendanceCalendarDto> GetAsync(
        int studentId,
        string? requestedPeriod,
        CancellationToken cancellationToken = default) =>
        GetCalendarAsync(studentId, requestedPeriod, allowInactive: false, cancellationToken);

    public Task<AttendanceCalendarDto> GetForStaffAsync(
        int studentId,
        string? requestedPeriod,
        CancellationToken cancellationToken = default) =>
        GetCalendarAsync(studentId, requestedPeriod, allowInactive: true, cancellationToken);

    private async Task<AttendanceCalendarDto> GetCalendarAsync(
        int studentId,
        string? requestedPeriod,
        bool allowInactive,
        CancellationToken cancellationToken)
    {
        var today = GymCalendar.Today(clock.UtcNow);
        var currentPeriod = PaymentPeriod.From(today);
        PaymentPeriod selectedPeriod;
        try
        {
            selectedPeriod = requestedPeriod is null
                ? currentPeriod
                : PaymentPeriod.Parse(requestedPeriod);
        }
        catch (DomainValidationException)
        {
            throw new AttendanceException(400, "invalid_attendance_period", "Period must use the format YYYY-MM.");
        }

        if (selectedPeriod > currentPeriod)
        {
            throw new AttendanceException(400, "invalid_attendance_period", "A future attendance period cannot be requested.");
        }

        var student = await students.GetByIdAsync(studentId, cancellationToken)
            ?? throw new NotFoundException("Student not found.");
        if (!student.IsActive && !allowInactive)
        {
            throw new AttendanceException(409, "student_inactive", "The student is inactive.");
        }

        var selectedMonth = await GetMonthAsync(studentId, selectedPeriod, cancellationToken);
        var credits = selectedMonth.Attendances.Any(item => item.ChargeKind == AttendanceChargeKind.Recovery)
            ? await recoveries.GetCreditsAsync(studentId, cancellationToken)
            : [];
        var origins = credits.ToDictionary(item => item.Credit.Id, item => item.Credit.OriginPeriod);
        var calendarAttendances = selectedMonth.Attendances.Select(item =>
        {
            string? origin = null;
            if (item.ChargeKind == AttendanceChargeKind.Recovery)
            {
                if (item.RecoveryCreditId is not { } creditId || !origins.TryGetValue(creditId, out origin))
                {
                    throw new InvalidOperationException("A recovery attendance does not reference its origin credit.");
                }
            }

            return AttendanceDto.From(item, origin);
        }).ToArray();

        var periodSummary = BuildPeriodSummary(selectedMonth);
        var currentSummary = student.IsActive
            ? await GetCurrentSummaryAsync(student, currentPeriod, today, cancellationToken)
            : null;
        return new AttendanceCalendarDto(
            GymCalendar.TimeZoneId,
            today,
            selectedPeriod.ToString(),
            periodSummary,
            currentSummary,
            calendarAttendances);
    }

    private async Task<AttendanceSummaryDto?> GetCurrentSummaryAsync(
        Student student,
        PaymentPeriod period,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var planId = student.PlanIdOn(today);
        if (planId is null || student.MonthlyMembershipStartedOn is not { } startedOn || startedOn > today)
        {
            return null;
        }

        var plan = await plans.GetByIdAsync(planId.Value, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        var limits = (await plans.GetAttendanceLimitsAsync([planId.Value], cancellationToken))
            .Where(item => item.EffectiveFrom <= today)
            .MaxBy(item => item.EffectiveFrom);
        if (limits is null)
        {
            return null;
        }

        var month = await GetMonthAsync(student.Id, period, cancellationToken);
        var snapshot = month.Snapshot;
        var weeklyLimit = snapshot?.WeeklyLimit ?? limits.WeeklyDays;
        var monthlyLimit = snapshot?.MonthlyLimit ?? limits.MonthlyDays;
        var planName = snapshot?.PlanName ?? plan.Name;
        var monthSummary = RecoveryAccounting.SummarizeMonth(
            monthlyLimit,
            month.Usage.OrdinaryAttended,
            month.Usage.RecoveryAttended,
            month.Usage.RecoveryUsedFromOrigin,
            month.Usage.RecoveryHeld);
        var weekStart = GymCalendar.WeekStart(today);
        var weekDates = await attendances.GetAttendanceDatesAsync(
            student.Id, weekStart, today.AddDays(1), cancellationToken);
        var weeklyConsumed = weekDates.Distinct().Count();

        return new AttendanceSummaryDto(
            GymCalendar.TimeZoneId,
            today,
            period.ToString(),
            planName,
            new WeekAttendanceSummaryDto(
                weekStart,
                GymCalendar.WeekEndExclusive(today).AddDays(-1),
                weeklyLimit,
                weeklyConsumed,
                Math.Max(weeklyLimit - weeklyConsumed, 0),
                Math.Max(weeklyConsumed - weeklyLimit, 0)),
            new MonthAttendanceSummaryDto(
                monthlyLimit,
                monthSummary.Consumed,
                monthSummary.Remaining,
                monthSummary.Exceeded,
                monthSummary.Attended,
                monthSummary.OrdinaryAttended,
                monthSummary.RecoveryAttended,
                month.Usage.RecoveryHeld));
    }

    private async Task<CalendarMonthData> GetMonthAsync(
        int studentId,
        PaymentPeriod period,
        CancellationToken cancellationToken)
    {
        var fromInclusive = period.FirstDay;
        var toExclusive = period.Next().FirstDay;
        var name = period.ToString();
        var snapshot = await attendances.GetPeriodAsync(studentId, name, cancellationToken);
        var monthAttendances = await attendances.GetAttendancesAsync(
            studentId, fromInclusive, toExclusive, cancellationToken);
        if (monthAttendances.Count > 0 && snapshot is null)
        {
            throw new InvalidOperationException("An attendance exists without its monthly snapshot.");
        }

        var usage = snapshot is null
            ? new RecoveryMonthUsage(
                monthAttendances.Count(item => item.ChargeKind == AttendanceChargeKind.Ordinary),
                monthAttendances.Count(item => item.ChargeKind == AttendanceChargeKind.Recovery),
                0,
                0)
            : await recoveries.GetMonthUsageAsync(studentId, snapshot.Id, name, cancellationToken);
        return new CalendarMonthData(snapshot, monthAttendances, usage);
    }

    private static AttendanceCalendarPeriodSummaryDto BuildPeriodSummary(CalendarMonthData month)
    {
        var summary = RecoveryAccounting.SummarizeMonth(
            month.Snapshot?.MonthlyLimit ?? 0,
            month.Usage.OrdinaryAttended,
            month.Usage.RecoveryAttended,
            month.Usage.RecoveryUsedFromOrigin,
            month.Usage.RecoveryHeld);
        var snapshot = month.Snapshot;
        return new AttendanceCalendarPeriodSummaryDto(
            snapshot?.PlanName,
            snapshot?.WeeklyLimit,
            snapshot?.MonthlyLimit,
            summary.Consumed,
            snapshot is null ? null : summary.Remaining,
            snapshot is null ? null : summary.Exceeded,
            summary.Attended,
            summary.OrdinaryAttended,
            summary.RecoveryAttended,
            month.Usage.RecoveryHeld);
    }

    private sealed record CalendarMonthData(
        AttendancePeriod? Snapshot,
        IReadOnlyList<CekideApi.Domain.Attendance> Attendances,
        RecoveryMonthUsage Usage);
}
