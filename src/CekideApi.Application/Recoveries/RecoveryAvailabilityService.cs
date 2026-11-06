using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Recoveries;

public sealed class RecoveryAvailabilityService(
    IRecoveryRepository recoveries,
    IStudentRepository students,
    IPlanRepository plans,
    RecoveryAccrualService accrual,
    RecoveryStateService state,
    IClock clock)
{
    public async Task<RecoveryCreditsDto> GetCreditsAsync(int studentId, CancellationToken cancellationToken = default)
    {
        await accrual.CloseDueWeeksForStudentAsync(studentId, cancellationToken);
        await state.NormalizeStudentAsync(studentId, cancellationToken);
        var now = clock.UtcNow;
        var today = GymCalendar.Today(now);
        var current = PaymentPeriod.From(today);
        var previous = PaymentPeriod.From(current.FirstDay.AddMonths(-1));
        var rows = await recoveries.GetCreditsAsync(studentId, cancellationToken);
        var credits = rows
            .Where(row => IsVisible(row.Credit, current, previous, today))
            .OrderBy(row => row.Credit.OriginPeriod, StringComparer.Ordinal)
            .ThenBy(row => row.Credit.Id)
            .Select(row => MapCredit(row, today))
            .ToArray();

        return new RecoveryCreditsDto(GymCalendar.TimeZoneId, now, today, credits);
    }

    public async Task<RecoveryAvailabilityDto> GetAvailabilityAsync(
        int studentId,
        int creditId,
        string? periodText,
        CancellationToken cancellationToken = default)
    {
        if (creditId <= 0)
        {
            throw new AttendanceException(400, "invalid_recovery_request", "A valid credit id is required.");
        }

        var period = ParsePeriod(periodText);
        await accrual.CloseDueWeeksForStudentAsync(studentId, cancellationToken);
        await state.NormalizeStudentAsync(studentId, cancellationToken);
        var row = (await recoveries.GetCreditsAsync(studentId, cancellationToken))
            .SingleOrDefault(item => item.Credit.Id == creditId)
            ?? throw new NotFoundException("Recovery credit not found.");
        var now = clock.UtcNow;
        var today = GymCalendar.Today(now);
        var credit = row.Credit;
        var allowedPeriods = GetAllowedPeriods(credit, today);
        if (credit.Status is not (RecoveryCreditStatus.Available or RecoveryCreditStatus.Reserved))
        {
            throw new AttendanceException(409, "recovery_credit_unavailable", "The recovery credit is not available.");
        }

        if (!allowedPeriods.Contains(period.ToString(), StringComparer.Ordinal))
        {
            throw new AttendanceException(400, "recovery_period_not_allowed", "The requested period is outside this credit's allowed windows.");
        }

        var windows = GetWindows(credit);
        var allowedDates = GetAllowedDates(credit, period, today);
        var monthStart = period.FirstDay;
        var monthEnd = period.Next().FirstDay;
        var range = GetCalendarRange(windows, monthStart, monthEnd);
        var shiftRows = await recoveries.GetShiftsAsync(monthStart, monthEnd, cancellationToken);
        var allowedDateSet = allowedDates.ToHashSet();
        var resultShifts = new List<RecoveryShiftDto>();
        var localNow = TimeZoneInfo.ConvertTime(now, GymCalendar.TimeZone);
        var localTime = TimeOnly.FromDateTime(localNow.DateTime);
        foreach (var shift in shiftRows.Where(item => allowedDateSet.Contains(item.LocalDate)))
        {
            var plannedIds = await recoveries.GetPlannedStudentIdsAsync(shift.Id, cancellationToken);
            var hasAttendance = await recoveries.HasAttendanceAsync(studentId, shift.LocalDate, cancellationToken);
            var hasHabitual = await recoveries.HasHabitualOccupancyAsync(studentId, shift.LocalDate, cancellationToken);
            var hasReservation = await recoveries.HasConfirmedReservationAsync(studentId, shift.LocalDate, cancellationToken);
            var unavailableReason = GetUnavailableReason(
                shift, credit, plannedIds.Count, hasAttendance, hasHabitual, hasReservation, today, localTime);
            resultShifts.Add(new RecoveryShiftDto(
                shift.Id,
                shift.LocalDate,
                shift.StartsAt,
                shift.EndsAt,
                ToUtc(shift.LocalDate, shift.StartsAt),
                ToUtc(shift.LocalDate, shift.EndsAt),
                shift.Capacity,
                plannedIds.Count,
                Math.Max(0, shift.Capacity - plannedIds.Count),
                unavailableReason is null,
                unavailableReason));
        }

        return new RecoveryAvailabilityDto(
            GymCalendar.TimeZoneId,
            now,
            today,
            credit.Id,
            credit.OriginPeriod,
            period.ToString(),
            windows,
            allowedPeriods,
            allowedDates,
            range,
            resultShifts);
    }

    public async Task<IReadOnlyList<RecoveryReservationDto>> GetReservationsAsync(
        int studentId,
        string? periodText,
        CancellationToken cancellationToken = default)
    {
        await accrual.CloseDueWeeksForStudentAsync(studentId, cancellationToken);
        await state.NormalizeStudentAsync(studentId, cancellationToken);
        var today = GymCalendar.Today(clock.UtcNow);
        var current = PaymentPeriod.From(today);
        var period = periodText is null ? current : ParsePeriod(periodText);
        var next = current.Next();
        var data = await recoveries.GetReservationsAsync(
            studentId, period.FirstDay, period.Next().FirstDay, cancellationToken);
        if (period > current && period != next)
        {
            throw new AttendanceException(400, "recovery_period_not_allowed", "The requested period is not available.");
        }

        if (period == next)
        {
            var rows = await recoveries.GetCreditsAsync(studentId, cancellationToken);
            var authorized = rows.Any(row =>
                row.Credit.Status is RecoveryCreditStatus.Available or RecoveryCreditStatus.Reserved &&
                GetAllowedPeriods(row.Credit, today).Contains(period.ToString(), StringComparer.Ordinal));
            if (!authorized && data.Count == 0)
            {
                throw new AttendanceException(400, "recovery_period_not_allowed", "The requested period is not available.");
            }
        }

        return data.Select(row => MapReservation(row, clock.UtcNow)).ToArray();
    }

    public async Task<IReadOnlyList<RecoveryReservationDto>> GetStudentReservationsAsync(
        int studentId,
        string? periodText,
        CancellationToken cancellationToken = default)
    {
        if (studentId <= 0)
        {
            throw new AttendanceException(400, "invalid_recovery_request", "A valid student id is required.");
        }

        if (await students.GetByIdAsync(studentId, cancellationToken) is null)
        {
            throw new NotFoundException("Student not found.");
        }

        var today = GymCalendar.Today(clock.UtcNow);
        var period = periodText is null ? PaymentPeriod.From(today) : ParsePeriod(periodText);
        var rows = await recoveries.GetStudentReservationsAsync(
            studentId, period.FirstDay, period.Next().FirstDay, cancellationToken);
        return rows.Select(row => MapReservation(row, clock.UtcNow)).ToArray();
    }

    internal async Task EnsureStudentCanReserveAsync(
        int studentId,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var student = await students.GetByIdAsync(studentId, cancellationToken)
            ?? throw new NotFoundException("Student not found.");
        if (!student.IsActive)
        {
            throw new AttendanceException(409, "student_inactive", "An inactive student cannot reserve a recovery.");
        }

        var planId = student.PlanIdOn(localDate);
        if (planId is null || student.MonthlyMembershipStartedOn is not { } startedOn || startedOn > localDate ||
            await plans.GetByIdAsync(planId.Value, cancellationToken) is null)
        {
            throw new AttendanceException(409, "monthly_plan_required", "The student has no applicable monthly plan.");
        }

        var limits = (await plans.GetAttendanceLimitsAsync([planId.Value], cancellationToken))
            .Where(candidate => candidate.EffectiveFrom <= localDate)
            .MaxBy(candidate => candidate.EffectiveFrom);
        if (limits is null)
        {
            throw new AttendanceException(409, "attendance_limits_missing", "Attendance limits are not configured.");
        }
    }

    internal static PaymentPeriod ParsePeriod(string? periodText)
    {
        try
        {
            return PaymentPeriod.Parse(periodText);
        }
        catch (DomainValidationException)
        {
            throw new AttendanceException(400, "invalid_period", "Period must use the YYYY-MM format.");
        }
    }

    internal static IReadOnlyList<RecoveryWindowDto> GetWindows(RecoveryCredit credit)
    {
        var windows = new List<RecoveryWindowDto>(2);
        if (credit.NormalStart is { } normalStart && credit.NormalEndExclusive is { } normalEnd)
        {
            windows.Add(new RecoveryWindowDto(normalStart, normalEnd, RecoveryWindowKind.Normal));
        }

        if (credit.ExceptionStart is { } exceptionStart && credit.ExceptionEndExclusive is { } exceptionEnd)
        {
            windows.Add(new RecoveryWindowDto(exceptionStart, exceptionEnd, RecoveryWindowKind.Exception));
        }

        return windows;
    }

    internal static IReadOnlyList<string> GetAllowedPeriods(RecoveryCredit credit, DateOnly today)
    {
        if (credit.Status is not (RecoveryCreditStatus.Available or RecoveryCreditStatus.Reserved))
        {
            return [];
        }

        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var window in GetWindows(credit).Where(window => window.EndExclusive > today))
        {
            var first = PaymentPeriod.From(window.Start > today ? window.Start : today);
            var last = PaymentPeriod.From(window.EndExclusive.AddDays(-1));
            for (var period = first; period <= last; period = period.Next())
            {
                result.Add(period.ToString());
            }
        }

        return result.ToArray();
    }

    private static bool IsVisible(
        RecoveryCredit credit,
        PaymentPeriod current,
        PaymentPeriod previous,
        DateOnly today)
    {
        if (credit.Status is RecoveryCreditStatus.Available or RecoveryCreditStatus.Reserved)
        {
            return true;
        }

        return (credit.Status is RecoveryCreditStatus.Used or RecoveryCreditStatus.Expired) &&
            (credit.OriginPeriod == current.ToString() || credit.OriginPeriod == previous.ToString()) &&
            GetWindows(credit).Any(window => window.EndExclusive > today);
    }

    private static RecoveryCreditDto MapCredit(RecoveryCreditData row, DateOnly today) =>
        new(
            row.Credit.Id,
            row.Credit.OriginPeriod,
            row.Week.WeekStart,
            row.Week.WeekEndExclusive,
            row.Credit.Status,
            GetWindows(row.Credit),
            GetAllowedPeriods(row.Credit, today),
            row.Credit.ExpiresAt,
            row.ReservationId);

    private static IReadOnlyList<DateOnly> GetAllowedDates(
        RecoveryCredit credit,
        PaymentPeriod period,
        DateOnly today)
    {
        var monthStart = period.FirstDay;
        var monthEnd = period.Next().FirstDay;
        var dates = new SortedSet<DateOnly>();
        foreach (var window in GetWindows(credit))
        {
            var start = Max(Max(window.Start, monthStart), today);
            var end = Min(window.EndExclusive, monthEnd);
            for (var date = start; date < end; date = date.AddDays(1))
            {
                if (GymCalendar.IsRecoveryWeekday(date))
                {
                    dates.Add(date);
                }
            }
        }

        return dates.ToArray();
    }

    private static RecoveryCalendarRangeDto GetCalendarRange(
        IReadOnlyList<RecoveryWindowDto> windows,
        DateOnly monthStart,
        DateOnly monthEnd)
    {
        var intersections = windows
            .Select(window => (Start: Max(window.Start, monthStart), End: Min(window.EndExclusive, monthEnd)))
            .Where(window => window.Start < window.End)
            .ToArray();
        return intersections.Length == 0
            ? new RecoveryCalendarRangeDto(monthStart, monthEnd)
            : new RecoveryCalendarRangeDto(intersections.Min(window => window.Start), intersections.Max(window => window.End));
    }

    private static string? GetUnavailableReason(
        GymShift shift,
        RecoveryCredit credit,
        int plannedCount,
        bool hasAttendance,
        bool hasHabitual,
        bool hasReservation,
        DateOnly today,
        TimeOnly localTime)
    {
        if (shift.Status == GymShiftStatus.Cancelled)
        {
            return "cancelled";
        }

        if (hasAttendance)
        {
            return "already_attended";
        }

        if (hasHabitual || hasReservation)
        {
            return "already_planned";
        }

        if (credit.Status == RecoveryCreditStatus.Reserved)
        {
            return "credit_reserved";
        }

        if (shift.LocalDate < today || shift.LocalDate == today && shift.StartsAt <= localTime)
        {
            return "started";
        }

        return plannedCount >= shift.Capacity ? "full" : null;
    }

    internal static RecoveryReservationDto MapReservation(RecoveryReservationData row, DateTimeOffset now)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, GymCalendar.TimeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        var localTime = TimeOnly.FromDateTime(localNow.DateTime);
        var canCancel = row.Reservation.Status == RecoveryReservationStatus.Confirmed &&
            row.Shift.Status == GymShiftStatus.Open &&
            (row.Shift.LocalDate > localDate ||
             row.Shift.LocalDate == localDate && row.Shift.StartsAt > localTime);
        return new RecoveryReservationDto(
            row.Reservation.Id,
            row.Credit.Id,
            row.Credit.OriginPeriod,
            row.Shift.Id,
            row.Reservation.LocalDate,
            row.Shift.StartsAt,
            row.Shift.EndsAt,
            row.Reservation.Status,
            row.Reservation.CancelledReason,
            row.Reservation.ConfirmedAt,
            row.Reservation.AttendedAt,
            row.Reservation.CancelledAt,
            canCancel,
            row.StudentName);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, GymCalendar.TimeZone), TimeSpan.Zero);
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
}
