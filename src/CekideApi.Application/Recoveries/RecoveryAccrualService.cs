using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Recoveries;

public sealed class RecoveryAccrualService(
    IRecoveryRepository recoveries,
    IStudentRepository students,
    IPlanRepository plans,
    IAttendanceRepository attendances,
    IAttendanceTransaction transaction,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public Task<bool> CaptureCurrentWeekForStudentAsync(
        int studentId,
        CancellationToken cancellationToken = default)
    {
        var weekStart = GymCalendar.WeekStart(GymCalendar.Today(clock.UtcNow));
        return CaptureWeekAsync(studentId, weekStart, cancellationToken);
    }

    public async Task<int> CaptureCurrentWeekAsync(CancellationToken cancellationToken = default)
    {
        var weekStart = GymCalendar.WeekStart(GymCalendar.Today(clock.UtcNow));
        var studentIds = await recoveries.GetStudentIdsAsync(cancellationToken);
        var captured = 0;
        foreach (var studentId in studentIds)
        {
            if (await CaptureWeekAsync(studentId, weekStart, cancellationToken))
            {
                captured++;
            }
        }

        return captured;
    }

    public async Task<int> CaptureCurrentWeekForPlanAsync(int planId, CancellationToken cancellationToken = default)
    {
        if (planId <= 0)
        {
            throw new DomainValidationException("Plan id must be positive.");
        }

        var weekStart = GymCalendar.WeekStart(GymCalendar.Today(clock.UtcNow));
        var studentIds = await recoveries.GetStudentIdsAsync(cancellationToken);
        var captured = 0;
        foreach (var studentId in studentIds)
        {
            var student = await students.GetByIdAsync(studentId, cancellationToken);
            if (student?.IsActive == true && student.PlanIdOn(weekStart) == planId &&
                await CaptureWeekAsync(studentId, weekStart, cancellationToken))
            {
                captured++;
            }
        }

        return captured;
    }

    public Task<bool> CaptureWeekAsync(
        int studentId,
        DateOnly weekStart,
        CancellationToken cancellationToken = default)
    {
        var today = GymCalendar.Today(clock.UtcNow);
        if (studentId <= 0 || weekStart == default || GymCalendar.WeekStart(weekStart) != weekStart)
        {
            throw new DomainValidationException("Student id and Monday recovery week start are required.");
        }

        if (weekStart != GymCalendar.WeekStart(today))
        {
            return Task.FromResult(false);
        }

        return transaction.ExecuteForStudentAsync(studentId, async token =>
        {
            if (await recoveries.GetWeekAsync(studentId, weekStart, token) is not null)
            {
                return false;
            }

            var student = await students.GetByIdAsync(studentId, token)
                ?? throw new NotFoundException($"Student {studentId} was not found.");
            var planId = student.PlanIdOn(weekStart);
            if (planId is null)
            {
                return false;
            }

            var plan = await plans.GetByIdAsync(planId.Value, token)
                ?? throw new InvalidOperationException($"Student {studentId} references a missing plan.");
            var planLimits = await plans.GetAttendanceLimitsAsync([planId.Value], token);
            var originPeriod = PaymentPeriod.From(weekStart).ToString();
            var snapshot = await attendances.GetPeriodAsync(studentId, originPeriod, token);

            PlanAttendanceLimits? limits;
            if (snapshot is not null)
            {
                limits = planLimits.SingleOrDefault(candidate => candidate.Id == snapshot.PlanLimitsId);
                if (limits is null)
                {
                    throw new InvalidOperationException(
                        $"Attendance period {snapshot.Id} references missing plan limits {snapshot.PlanLimitsId}.");
                }
            }
            else
            {
                limits = planLimits
                    .Where(candidate => candidate.EffectiveFrom <= weekStart)
                    .MaxBy(candidate => candidate.EffectiveFrom)
                    ?? planLimits.MinBy(candidate => candidate.EffectiveFrom);
                if (limits is null)
                {
                    return false;
                }

                snapshot = AttendancePeriod.Create(
                    studentId,
                    plan.Id,
                    limits.Id,
                    plan.Name,
                    limits.WeeklyDays,
                    limits.MonthlyDays,
                    weekStart);
                attendances.AddPeriod(snapshot);
                await unitOfWork.SaveChangesAsync(token);
            }

            var eligible = student.IsActive &&
                student.MonthlyMembershipStartedOn is { } startedOn && startedOn <= weekStart &&
                limits.EffectiveFrom <= weekStart;
            var week = RecoveryWeek.Create(
                studentId,
                weekStart,
                snapshot.Id,
                limits.Id,
                snapshot.WeeklyLimit,
                eligible ? RecoveryWeekEligibility.Eligible : RecoveryWeekEligibility.Excluded);
            recoveries.AddWeek(week);
            return true;
        }, cancellationToken);
    }

    public async Task<int> CloseDueWeeksForStudentAsync(
        int studentId,
        CancellationToken cancellationToken = default)
    {
        var today = GymCalendar.Today(clock.UtcNow);
        var currentWeekStart = GymCalendar.WeekStart(today);
        await CaptureWeekAsync(studentId, currentWeekStart, cancellationToken);

        return await transaction.ExecuteForStudentAsync(studentId, async token =>
        {
            var dueWeeks = await recoveries.GetWeeksToCloseAsync(studentId, currentWeekStart, token);
            var orderedWeeks = RecoveryAccounting.OrderWeeksForClosing(
                dueWeeks.Select(week => week.WeekStart));
            var closed = 0;
            foreach (var weekStart in orderedWeeks)
            {
                var week = dueWeeks.Single(candidate => candidate.WeekStart == weekStart);
                var attendanceDates = await attendances.GetAttendanceDatesAsync(
                    studentId, week.WeekStart, week.WeekEndExclusive, token);
                var balance = await recoveries.GetOriginBalanceAsync(
                    studentId, week.AttendancePeriodId, week.OriginPeriod, token);
                var monthlyLimit = (await attendances.GetPeriodAsync(
                    studentId, week.OriginPeriod, token))?.MonthlyLimit
                    ?? throw new InvalidOperationException(
                        $"Recovery week {week.Id} references a missing origin attendance period.");
                var accounting = RecoveryAccounting.CalculateClose(
                    week.WeeklyTarget,
                    attendanceDates.Count,
                    monthlyLimit,
                    balance.Consumed,
                    balance.Held);
                var granted = week.Eligibility == RecoveryWeekEligibility.Eligible
                    ? accounting.Granted
                    : 0;
                week.Close(clock.UtcNow, attendanceDates.Count, granted);
                for (var ordinal = 1; ordinal <= granted; ordinal++)
                {
                    recoveries.AddCredit(RecoveryCredit.Create(week, ordinal));
                }

                closed++;
            }

            return closed;
        }, cancellationToken);
    }

    public async Task<int> CloseDueWeeksAsync(CancellationToken cancellationToken = default)
    {
        var studentIds = await recoveries.GetStudentIdsAsync(cancellationToken);
        var closed = 0;
        foreach (var studentId in studentIds)
        {
            closed += await CloseDueWeeksForStudentAsync(studentId, cancellationToken);
        }

        return closed;
    }
}
