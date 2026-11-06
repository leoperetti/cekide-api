namespace CekideApi.Domain;

public enum RecoveryReservationStatus
{
    Confirmed,
    Attended,
    Cancelled,
    Missed
}

public enum RecoveryReservationCancelReason
{
    Student,
    ShiftCancelled
}

public sealed class RecoveryReservation : Entity
{
    private RecoveryReservation()
    {
    }

    public int StudentId { get; private set; }

    public int RecoveryCreditId { get; private set; }

    public int ShiftId { get; private set; }

    public DateOnly LocalDate { get; private set; }

    public RecoveryReservationStatus Status { get; private set; }

    public RecoveryReservationCancelReason? CancelledReason { get; private set; }

    public DateTime ConfirmedAt { get; private set; }

    public DateTime? AttendedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public static RecoveryReservation Create(
        int studentId,
        RecoveryCredit credit,
        GymShift shift,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(credit);
        ArgumentNullException.ThrowIfNull(shift);
        if (studentId <= 0 || credit.StudentId != studentId || credit.Id <= 0 || shift.Id <= 0 ||
            shift.LocalDate == default || now == default || !credit.IsAllowedDate(shift.LocalDate))
        {
            throw new DomainValidationException("Recovery reservation references are invalid.");
        }

        return new RecoveryReservation
        {
            StudentId = studentId,
            RecoveryCreditId = credit.Id,
            ShiftId = shift.Id,
            LocalDate = shift.LocalDate,
            Status = RecoveryReservationStatus.Confirmed,
            ConfirmedAt = now.UtcDateTime
        };
    }

    public void MarkAttended(DateTimeOffset now)
    {
        if (Status != RecoveryReservationStatus.Confirmed || now == default)
        {
            throw new ConflictException("Only a confirmed recovery reservation can be attended.");
        }

        Status = RecoveryReservationStatus.Attended;
        AttendedAt = now.UtcDateTime;
    }

    public void Cancel(RecoveryReservationCancelReason reason, DateTimeOffset now)
    {
        if (Status == RecoveryReservationStatus.Cancelled)
        {
            return;
        }

        if (Status != RecoveryReservationStatus.Confirmed || now == default || !Enum.IsDefined(reason))
        {
            throw new ConflictException("Recovery reservation cannot be cancelled.");
        }

        Status = RecoveryReservationStatus.Cancelled;
        CancelledReason = reason;
        CancelledAt = now.UtcDateTime;
    }

    public void MarkMissed(DateTimeOffset now)
    {
        if (Status != RecoveryReservationStatus.Confirmed || now == default)
        {
            throw new ConflictException("Only a confirmed recovery reservation can be marked missed.");
        }

        Status = RecoveryReservationStatus.Missed;
    }
}
