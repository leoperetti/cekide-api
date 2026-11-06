namespace CekideApi.Domain;

public sealed record AttendanceQuotaSnapshot(
    int WeeklyConsumed,
    int MonthlyConsumed,
    bool WeeklyExceeded,
    bool MonthlyExceeded,
    bool MonthlyExhausted);

public static class AttendanceRules
{
    public static AttendanceQuotaSnapshot EvaluateAfterCheckIn(
        DateOnly localDate,
        IEnumerable<DateOnly> previousAttendanceDates,
        int weeklyLimit,
        int monthlyLimit)
    {
        ArgumentNullException.ThrowIfNull(previousAttendanceDates);

        if (weeklyLimit is < 1 or > 7 || monthlyLimit < 1)
        {
            throw new DomainValidationException("Attendance limits must be positive and the weekly limit cannot exceed seven.");
        }

        var dates = previousAttendanceDates.ToArray();
        if (dates.Contains(localDate))
        {
            throw new ConflictException("An attendance already exists for this local date.");
        }

        var weekStart = GymCalendar.WeekStart(localDate);
        var monthStart = GymCalendar.MonthStart(localDate);

        var weeklyConsumed = dates.Count(date =>
            date >= weekStart && date < localDate) + 1;
        var monthlyConsumed = dates.Count(date =>
            date >= monthStart && date < localDate) + 1;

        return new AttendanceQuotaSnapshot(
            weeklyConsumed,
            monthlyConsumed,
            weeklyConsumed > weeklyLimit,
            monthlyConsumed > monthlyLimit,
            monthlyConsumed >= monthlyLimit);
    }
}
