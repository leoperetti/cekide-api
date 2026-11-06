using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Recoveries;

public sealed class RecoveryReservationService(
    IRecoveryRepository recoveries,
    IShiftRepository shifts,
    RecoveryAccrualService accrual,
    RecoveryStateService state,
    RecoveryAvailabilityService availability,
    IRecoveryTransaction transaction,
    IClock clock)
{
    public async Task<(RecoveryReservationDto Reservation, bool Created)> ReserveAsync(
        int studentId,
        CreateRecoveryReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.CreditId <= 0 || request.ShiftId <= 0)
        {
            throw new AttendanceException(400, "invalid_recovery_request", "Credit and shift ids must be positive.");
        }

        await accrual.CloseDueWeeksForStudentAsync(studentId, cancellationToken);
        await state.NormalizeStudentAsync(studentId, cancellationToken);
        var now = clock.UtcNow;

        var result = await transaction.ExecuteForStudentAndShiftAsync(
            studentId,
            request.ShiftId,
            async token =>
            {
                var existing = await recoveries.GetConfirmedReservationAsync(
                    studentId, request.CreditId, request.ShiftId, token);
                var shift = await shifts.GetByIdAsync(request.ShiftId, token)
                    ?? throw new NotFoundException("Shift not found.");
                if (existing is not null)
                {
                    var existingCredit = await recoveries.GetCreditAsync(studentId, existing.RecoveryCreditId, token)
                        ?? throw new InvalidOperationException("A recovery reservation references a missing credit.");
                    var week = (await recoveries.GetCreditsAsync(studentId, token))
                        .Single(row => row.Credit.Id == existingCredit.Id).Week;
                    return (Reservation: existing, Credit: existingCredit, Week: week, Shift: shift, Created: false);
                }

                var credit = await recoveries.GetCreditAsync(studentId, request.CreditId, token)
                    ?? throw new NotFoundException("Recovery credit not found.");
                if (credit.Status != RecoveryCreditStatus.Available ||
                    now.UtcDateTime >= credit.ExpiresAt)
                {
                    throw new AttendanceException(409, "recovery_credit_unavailable", "The recovery credit is not available.");
                }

                if (!credit.IsAllowedDate(shift.LocalDate))
                {
                    throw new AttendanceException(409, "recovery_window_closed", "The shift is outside this credit's windows.");
                }

                if (shift.Status == GymShiftStatus.Cancelled)
                {
                    throw new AttendanceException(409, "shift_unavailable", "The shift is cancelled.");
                }

                var localNow = TimeZoneInfo.ConvertTime(now, GymCalendar.TimeZone);
                var today = DateOnly.FromDateTime(localNow.DateTime);
                var time = TimeOnly.FromDateTime(localNow.DateTime);
                if (shift.LocalDate < today || shift.LocalDate == today && shift.StartsAt <= time)
                {
                    throw new AttendanceException(409, "shift_unavailable", "The shift has already started.");
                }

                await availability.EnsureStudentCanReserveAsync(studentId, shift.LocalDate, token);
                if (await recoveries.HasAttendanceAsync(studentId, shift.LocalDate, token))
                {
                    throw new AttendanceException(409, "already_attended", "The student already attended that day.");
                }

                if (await recoveries.HasHabitualOccupancyAsync(studentId, shift.LocalDate, token) ||
                    await recoveries.HasConfirmedReservationAsync(studentId, shift.LocalDate, token))
                {
                    throw new AttendanceException(409, "already_planned", "The student is already planned for that day.");
                }

                var plannedIds = await recoveries.GetPlannedStudentIdsAsync(shift.Id, token);
                if (plannedIds.Count >= shift.Capacity)
                {
                    throw new AttendanceException(409, "shift_full", "The shift has no planned places available.");
                }

                credit.Reserve(shift.LocalDate, now);
                var reservation = RecoveryReservation.Create(studentId, credit, shift, now);
                recoveries.AddReservation(reservation);
                var source = (await recoveries.GetCreditsAsync(studentId, token))
                    .Single(row => row.Credit.Id == credit.Id).Week;
                return (Reservation: reservation, Credit: credit, Week: source, Shift: shift, Created: true);
            },
            cancellationToken);
        return (
            RecoveryAvailabilityService.MapReservation(
                new RecoveryReservationData(result.Reservation, result.Credit, result.Week, result.Shift, null), now),
            result.Created);
    }

    public async Task<RecoveryReservationDto> CancelAsync(
        int studentId,
        int reservationId,
        CancellationToken cancellationToken = default)
    {
        if (reservationId <= 0)
        {
            throw new NotFoundException("Recovery reservation not found.");
        }

        var shiftId = await recoveries.GetReservationShiftIdAsync(studentId, reservationId, cancellationToken)
            ?? throw new NotFoundException("Recovery reservation not found.");
        await accrual.CloseDueWeeksForStudentAsync(studentId, cancellationToken);
        await state.NormalizeStudentAsync(studentId, cancellationToken);
        var now = clock.UtcNow;

        return await transaction.ExecuteForStudentAndShiftAsync(
            studentId,
            shiftId,
            async token =>
            {
                var reservation = await recoveries.GetReservationAsync(studentId, reservationId, token)
                    ?? throw new NotFoundException("Recovery reservation not found.");
                var credit = await recoveries.GetCreditAsync(studentId, reservation.RecoveryCreditId, token)
                    ?? throw new InvalidOperationException("A recovery reservation references a missing credit.");
                var shift = await shifts.GetByIdAsync(shiftId, token)
                    ?? throw new InvalidOperationException("A recovery reservation references a missing shift.");

                if (reservation.Status == RecoveryReservationStatus.Cancelled)
                {
                    return Map(reservation, credit, shift, now);
                }

                if (reservation.Status != RecoveryReservationStatus.Confirmed)
                {
                    throw new AttendanceException(409, "reservation_not_cancellable", "The reservation can no longer be cancelled.");
                }

                var localNow = TimeZoneInfo.ConvertTime(now, GymCalendar.TimeZone);
                var today = DateOnly.FromDateTime(localNow.DateTime);
                var time = TimeOnly.FromDateTime(localNow.DateTime);
                if (shift.LocalDate < today || shift.LocalDate == today && shift.StartsAt <= time)
                {
                    throw new AttendanceException(409, "reservation_not_cancellable", "The reservation has already started.");
                }

                reservation.Cancel(RecoveryReservationCancelReason.Student, now);
                credit.Release(today);
                return Map(reservation, credit, shift, now);
            },
            cancellationToken);
    }

    private static RecoveryReservationDto Map(
        RecoveryReservation reservation,
        RecoveryCredit credit,
        GymShift shift,
        DateTimeOffset now)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, GymCalendar.TimeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var time = TimeOnly.FromDateTime(localNow.DateTime);
        return new RecoveryReservationDto(
            reservation.Id,
            credit.Id,
            credit.OriginPeriod,
            shift.Id,
            reservation.LocalDate,
            shift.StartsAt,
            shift.EndsAt,
            reservation.Status,
            reservation.CancelledReason,
            reservation.ConfirmedAt,
            reservation.AttendedAt,
            reservation.CancelledAt,
            reservation.Status == RecoveryReservationStatus.Confirmed &&
                shift.Status == GymShiftStatus.Open &&
                (shift.LocalDate > today || shift.LocalDate == today && shift.StartsAt > time),
            null);
    }
}
