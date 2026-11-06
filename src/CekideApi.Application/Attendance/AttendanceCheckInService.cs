using CekideApi.Application.Abstractions;
using CekideApi.Application.Notifications;
using CekideApi.Application.Recoveries;
using CekideApi.Domain;
using AttendanceEntity = CekideApi.Domain.Attendance;

namespace CekideApi.Application.Attendance;

public sealed class AttendanceCheckInService(
    IStudentRepository students,
    IPlanRepository plans,
    IAttendanceRepository attendances,
    IKioskSessionRepository kiosks,
    IAttendanceTransaction transaction,
    IUnitOfWork unitOfWork,
    IClock clock,
    AttendanceQuotaNotificationService quotaNotifications,
    IShiftRepository? shifts = null,
    IRecoveryRepository? recoveries = null,
    RecoveryAccrualService? recoveryAccrual = null,
    RecoveryStateService? recoveryState = null)
{
    public Task<AttendanceCheckInResultDto> CheckInSelfAsync(
        int studentId,
        string? dni,
        CancellationToken cancellationToken = default) =>
        CheckInAsync(studentId, dni, AttendanceSource.Self, null, null, cancellationToken);

    public Task<AttendanceCheckInResultDto> CheckInKioskAsync(
        int studentId,
        string? dni,
        int kioskSessionId,
        string tenant,
        CancellationToken cancellationToken = default) =>
        CheckInAsync(studentId, dni, AttendanceSource.Kiosk, kioskSessionId, tenant, cancellationToken);

    public async Task<AttendanceCheckInResultDto> CheckInKioskByDniAsync(
        string? dni,
        int kioskSessionId,
        string tenant,
        CancellationToken cancellationToken = default)
    {
        string normalizedDni;
        try
        {
            normalizedDni = Student.NormalizeDni(dni);
        }
        catch (DomainValidationException)
        {
            throw new AttendanceException(400, "invalid_dni", "A valid DNI is required.");
        }

        var student = await students.GetByDniAsync(normalizedDni, cancellationToken);
        if (student is null)
        {
            throw new AttendanceException(404, "student_not_found", "Student not found.");
        }

        return await CheckInAsync(student.Id, normalizedDni, AttendanceSource.Kiosk, kioskSessionId, tenant, cancellationToken);
    }

    private async Task<AttendanceCheckInResultDto> CheckInAsync(
        int studentId,
        string? dni,
        AttendanceSource source,
        int? kioskSessionId,
        string? kioskTenant,
        CancellationToken cancellationToken)
    {
        string normalizedDni;
        try
        {
            normalizedDni = Student.NormalizeDni(dni);
        }
        catch (DomainValidationException)
        {
            throw new AttendanceException(400, "invalid_dni", "A valid DNI is required.");
        }

        if (recoveryAccrual is not null && recoveryState is not null)
        {
            await recoveryAccrual.CloseDueWeeksForStudentAsync(studentId, cancellationToken);
            await recoveryState.NormalizeStudentAsync(studentId, cancellationToken);
        }

        var capturedAt = clock.UtcNow;
        var localDate = GymCalendar.Today(capturedAt);
        var periodName = PaymentPeriod.From(localDate).ToString();

        return await transaction.ExecuteForStudentAsync(studentId, async token =>
        {
            var student = await students.GetByIdAsync(studentId, token);
            if (student is null)
            {
                throw new NotFoundException("Student not found.");
            }

            if (!student.IsActive)
            {
                throw new AttendanceException(409, "student_inactive", "The student is inactive.");
            }

            if (source == AttendanceSource.Self &&
                !string.Equals(student.Dni, normalizedDni, StringComparison.Ordinal))
            {
                throw new ForbiddenException("The supplied DNI does not match the signed-in student.");
            }

            if (source == AttendanceSource.Kiosk)
            {
                var kiosk = kioskSessionId is { } id
                    ? await kiosks.GetByIdAsync(id, token)
                    : null;
                if (kiosk is null || !string.Equals(kiosk.Tenant, kioskTenant, StringComparison.Ordinal) ||
                    !kiosk.IsActive(capturedAt))
                {
                    throw new AttendanceException(401, "kiosk_not_authorized", "Kiosk session is not authorized.");
                }

                if (!string.Equals(student.Dni, normalizedDni, StringComparison.Ordinal))
                {
                    throw new NotFoundException("Student not found.");
                }
            }

            var planId = student.PlanIdOn(localDate);
            if (planId is null || student.MonthlyMembershipStartedOn is not { } startedOn || startedOn > localDate)
            {
                throw new AttendanceException(409, "monthly_plan_required", "The student has no applicable monthly plan.");
            }

            var plan = await plans.GetByIdAsync(planId.Value, token);
            if (plan is null)
            {
                throw new AttendanceException(409, "monthly_plan_required", "The student has no applicable monthly plan.");
            }

            var limits = (await plans.GetAttendanceLimitsAsync([planId.Value], token))
                .Where(limit => limit.EffectiveFrom <= localDate)
                .MaxBy(limit => limit.EffectiveFrom);
            if (limits is null)
            {
                throw new AttendanceException(409, "attendance_limits_missing", "Attendance limits are not configured.");
            }

            var existing = await attendances.GetForStudentDateAsync(studentId, localDate, token);
            var snapshot = await attendances.GetPeriodAsync(studentId, periodName, token);
            if (existing is not null && snapshot is null)
            {
                throw new InvalidOperationException("An attendance exists without its monthly snapshot.");
            }

            if (snapshot is null)
            {
                snapshot = AttendancePeriod.Create(
                    studentId,
                    plan.Id,
                    limits.Id,
                    plan.Name,
                    limits.WeeklyDays,
                    limits.MonthlyDays,
                    localDate);
                attendances.AddPeriod(snapshot);
                await unitOfWork.SaveChangesAsync(token);
            }

            var firstDate = Min(GymCalendar.WeekStart(localDate), GymCalendar.MonthStart(localDate));
            var datesThroughToday = await attendances.GetAttendanceDatesAsync(
                studentId, firstDate, localDate.AddDays(1), token);
            var previousDates = datesThroughToday.Where(date => date < localDate).ToArray();
            var quota = AttendanceRules.EvaluateAfterCheckIn(
                localDate, previousDates, snapshot.WeeklyLimit, snapshot.MonthlyLimit);
            var monthUsage = recoveries is null
                ? new RecoveryMonthUsage(datesThroughToday.Count, 0, 0, 0)
                : await recoveries.GetMonthUsageAsync(studentId, snapshot.Id, periodName, token);

            AttendanceEntity attendance;
            AttendanceCheckInOutcome outcome;
            AttendanceQuotaNotificationStatus? noticeStatus = null;
            string? recoveryOriginPeriod = null;
            var ordinaryAttended = monthUsage.OrdinaryAttended;
            var recoveryAttended = monthUsage.RecoveryAttended;
            var recoveryUsedFromOrigin = monthUsage.RecoveryUsedFromOrigin;
            var recoveryHeld = monthUsage.RecoveryHeld;
            if (existing is not null)
            {
                attendance = existing;
                outcome = AttendanceCheckInOutcome.AlreadyCheckedIn;
                if (existing.RecoveryCreditId is { } existingCreditId && recoveries is not null)
                {
                    recoveryOriginPeriod = (await recoveries.GetCreditAsync(studentId, existingCreditId, token))
                        ?.OriginPeriod;
                }

                noticeStatus = (await attendances.GetQuotaNotificationAsync(studentId, periodName, token))?.Status;
            }
            else
            {
                var localTime = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(capturedAt, GymCalendar.TimeZone).DateTime);
                var shiftId = shifts is null
                    ? null
                    : await shifts.FindMatchingOpenShiftAsync(localDate, localTime, token);
                RecoveryReservation? reservation = null;
                RecoveryCredit? recoveryCredit = null;
                if (shiftId is { } matchingShiftId && recoveries is not null)
                {
                    reservation = await recoveries.GetConfirmedReservationForShiftAsync(
                        studentId, matchingShiftId, localDate, token);
                    if (reservation is not null)
                    {
                        recoveryCredit = await recoveries.GetCreditAsync(
                            studentId, reservation.RecoveryCreditId, token);
                        if (recoveryCredit is null || recoveryCredit.Status != RecoveryCreditStatus.Reserved)
                        {
                            throw new InvalidOperationException(
                                "A confirmed recovery reservation does not reference a reserved credit.");
                        }

                        reservation.MarkAttended(capturedAt);
                        recoveryCredit.Use(capturedAt);
                        recoveryOriginPeriod = recoveryCredit.OriginPeriod;
                        recoveryAttended++;
                        if (recoveryOriginPeriod == periodName)
                        {
                            recoveryUsedFromOrigin++;
                            recoveryHeld--;
                        }
                    }
                }

                var chargeKind = reservation is null
                    ? AttendanceChargeKind.Ordinary
                    : AttendanceChargeKind.Recovery;
                var consumesCurrentOrigin = chargeKind == AttendanceChargeKind.Ordinary ||
                    string.Equals(recoveryOriginPeriod, periodName, StringComparison.Ordinal);
                var monthlyConsumedAfter = monthUsage.OrdinaryAttended + monthUsage.RecoveryUsedFromOrigin +
                    (consumesCurrentOrigin ? 1 : 0);
                var attendanceQuota = new AttendanceQuotaSnapshot(
                    quota.WeeklyConsumed,
                    monthlyConsumedAfter,
                    quota.WeeklyExceeded,
                    monthlyConsumedAfter > snapshot.MonthlyLimit,
                    monthlyConsumedAfter >= snapshot.MonthlyLimit);
                if (chargeKind == AttendanceChargeKind.Ordinary)
                {
                    ordinaryAttended++;
                }

                attendance = AttendanceEntity.Create(
                    studentId,
                    snapshot.Id,
                    localDate,
                    capturedAt,
                    source,
                    kioskSessionId,
                    attendanceQuota,
                    shiftId,
                    chargeKind,
                    recoveryCredit?.Id);
                attendances.AddAttendance(attendance);
                outcome = AttendanceCheckInOutcome.Created;

                if (AttendanceQuotaNotificationService.ShouldCreateForCheckIn(
                        chargeKind,
                        monthUsage,
                        monthlyConsumedAfter,
                        snapshot.MonthlyLimit))
                {
                    await quotaNotifications.CreateForExhaustedQuotaAsync(
                        student, snapshot, localDate, capturedAt, token);
                    noticeStatus = AttendanceQuotaNotificationStatus.Pending;
                }
                else
                {
                    noticeStatus = (await attendances.GetQuotaNotificationAsync(studentId, periodName, token))?.Status;
                }
            }

            if (outcome == AttendanceCheckInOutcome.Created)
            {
                await unitOfWork.SaveChangesAsync(token);
            }

            var weeklyDates = datesThroughToday
                .Where(date => date >= GymCalendar.WeekStart(localDate) && date <= localDate)
                .Distinct()
                .ToArray();
            var monthlyDates = datesThroughToday
                .Where(date => date >= GymCalendar.MonthStart(localDate) && date <= localDate)
                .Distinct()
                .ToArray();
            var weeklyConsumed = Math.Max(quota.WeeklyConsumed, weeklyDates.Length);
            var attendedCount = monthlyDates.Length + (outcome == AttendanceCheckInOutcome.Created ? 1 : 0);
            var monthly = RecoveryAccounting.SummarizeMonth(
                snapshot.MonthlyLimit,
                ordinaryAttended,
                recoveryAttended,
                recoveryUsedFromOrigin,
                Math.Max(0, recoveryHeld));
            var monthlyConsumed = monthly.Consumed;
            var summary = new AttendanceSummaryDto(
                GymCalendar.TimeZoneId,
                localDate,
                periodName,
                snapshot.PlanName,
                new WeekAttendanceSummaryDto(
                    GymCalendar.WeekStart(localDate),
                    GymCalendar.WeekEndExclusive(localDate).AddDays(-1),
                    snapshot.WeeklyLimit,
                    weeklyConsumed,
                    Math.Max(snapshot.WeeklyLimit - weeklyConsumed, 0),
                    Math.Max(weeklyConsumed - snapshot.WeeklyLimit, 0)),
                new MonthAttendanceSummaryDto(
                    snapshot.MonthlyLimit,
                    monthlyConsumed,
                    monthly.Remaining,
                    monthly.Exceeded,
                    attendedCount,
                    ordinaryAttended,
                    recoveryAttended,
                    Math.Max(0, recoveryHeld)));
            var warnings = new List<string>(2);
            if (quota.WeeklyExceeded)
            {
                warnings.Add("weekly_limit_exceeded");
            }

            if (monthlyConsumed >= snapshot.MonthlyLimit)
            {
                warnings.Add("monthly_quota_exhausted");
            }

            return new AttendanceCheckInResultDto(
                outcome, AttendanceDto.From(attendance, recoveryOriginPeriod), summary, warnings, noticeStatus);
        }, cancellationToken);
    }

    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
}
