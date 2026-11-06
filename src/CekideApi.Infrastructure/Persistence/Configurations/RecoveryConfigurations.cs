using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CekideApi.Infrastructure.Persistence.Configurations;

public sealed class RecoveryWeekConfiguration : IEntityTypeConfiguration<RecoveryWeek>
{
    public void Configure(EntityTypeBuilder<RecoveryWeek> builder)
    {
        builder.ToTable("recovery_weeks", table =>
        {
            table.HasCheckConstraint("CK_recovery_weeks_weekly_target", "weekly_target BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "CK_recovery_weeks_close_counts",
                "(closed_at IS NULL AND attended_count IS NULL AND granted_count IS NULL) OR " +
                "(closed_at IS NOT NULL AND attended_count IS NOT NULL AND attended_count >= 0 AND " +
                "granted_count IS NOT NULL AND granted_count BETWEEN 0 AND weekly_target)");
            table.HasCheckConstraint("CK_recovery_weeks_eligibility", "eligibility IN ('eligible', 'excluded')");
        });
        builder.HasKey(week => week.Id);
        builder.HasAlternateKey(week => new { week.StudentId, week.Id });
        builder.Property(week => week.WeekStart).HasColumnType("date").IsRequired();
        builder.Property(week => week.WeekEndExclusive).HasColumnType("date").IsRequired();
        builder.Property(week => week.OriginPeriod).HasColumnType("char(7)").IsRequired();
        builder.Property(week => week.Eligibility)
            .HasConversion(value => value == RecoveryWeekEligibility.Eligible ? "eligible" : "excluded",
                value => value == "eligible" ? RecoveryWeekEligibility.Eligible : RecoveryWeekEligibility.Excluded)
            .HasMaxLength(20)
            .IsRequired();
        builder.HasIndex(week => new { week.StudentId, week.WeekStart }).IsUnique();
        builder.HasOne<Student>().WithMany().HasForeignKey(week => week.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttendancePeriod>().WithMany()
            .HasForeignKey(week => new { week.StudentId, week.AttendancePeriodId })
            .HasPrincipalKey(period => new { period.StudentId, period.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanAttendanceLimits>().WithMany().HasForeignKey(week => week.PlanLimitsId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RecoveryCreditConfiguration : IEntityTypeConfiguration<RecoveryCredit>
{
    public void Configure(EntityTypeBuilder<RecoveryCredit> builder)
    {
        builder.ToTable("recovery_credits", table =>
        {
            table.HasCheckConstraint("CK_recovery_credits_ordinal", "ordinal > 0");
            table.HasCheckConstraint(
                "CK_recovery_credits_normal_window",
                "(normal_start IS NULL AND normal_end_exclusive IS NULL) OR " +
                "(normal_start IS NOT NULL AND normal_end_exclusive IS NOT NULL AND normal_start < normal_end_exclusive)");
            table.HasCheckConstraint(
                "CK_recovery_credits_exception_window",
                "(exception_start IS NULL AND exception_end_exclusive IS NULL) OR " +
                "(exception_start IS NOT NULL AND exception_end_exclusive IS NOT NULL AND exception_start < exception_end_exclusive)");
            table.HasCheckConstraint(
                "CK_recovery_credits_used_at",
                "(status = 'used' AND used_at IS NOT NULL) OR (status <> 'used' AND used_at IS NULL)");
            table.HasCheckConstraint(
                "CK_recovery_credits_status",
                "status IN ('available', 'reserved', 'used', 'expired')");
        });
        builder.HasKey(credit => credit.Id);
        builder.HasAlternateKey(credit => new { credit.StudentId, credit.Id });
        builder.Property(credit => credit.OriginPeriod).HasColumnType("char(7)").IsRequired();
        builder.Property(credit => credit.NormalStart).HasColumnType("date");
        builder.Property(credit => credit.NormalEndExclusive).HasColumnType("date");
        builder.Property(credit => credit.ExceptionStart).HasColumnType("date");
        builder.Property(credit => credit.ExceptionEndExclusive).HasColumnType("date");
        builder.Property(credit => credit.ExpiresAt).HasColumnType("datetime2").IsRequired();
        builder.Property(credit => credit.Status)
            .HasConversion(value => CreditStatusToDatabase(value), value => CreditStatusFromDatabase(value))
            .HasMaxLength(20)
            .IsRequired();
        builder.HasIndex(credit => new { credit.RecoveryWeekId, credit.Ordinal }).IsUnique();
        builder.HasIndex(credit => new { credit.StudentId, credit.Status });
        builder.HasOne<RecoveryWeek>().WithMany()
            .HasForeignKey(credit => new { credit.StudentId, credit.RecoveryWeekId })
            .HasPrincipalKey(week => new { week.StudentId, week.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttendancePeriod>().WithMany()
            .HasForeignKey(credit => new { credit.StudentId, credit.AttendancePeriodId })
            .HasPrincipalKey(period => new { period.StudentId, period.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanAttendanceLimits>().WithMany().HasForeignKey(credit => credit.PlanLimitsId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string CreditStatusToDatabase(RecoveryCreditStatus status) => status switch
    {
        RecoveryCreditStatus.Available => "available",
        RecoveryCreditStatus.Reserved => "reserved",
        RecoveryCreditStatus.Used => "used",
        RecoveryCreditStatus.Expired => "expired",
        _ => throw new InvalidOperationException("Recovery credit status is invalid.")
    };

    private static RecoveryCreditStatus CreditStatusFromDatabase(string status) => status switch
    {
        "available" => RecoveryCreditStatus.Available,
        "reserved" => RecoveryCreditStatus.Reserved,
        "used" => RecoveryCreditStatus.Used,
        "expired" => RecoveryCreditStatus.Expired,
        _ => throw new InvalidOperationException("Stored recovery credit status is invalid.")
    };
}

public sealed class RecoveryReservationConfiguration : IEntityTypeConfiguration<RecoveryReservation>
{
    public void Configure(EntityTypeBuilder<RecoveryReservation> builder)
    {
        builder.ToTable("recovery_reservations", table =>
        {
            table.HasCheckConstraint(
                "CK_recovery_reservations_status",
                "status IN ('confirmed', 'attended', 'cancelled', 'missed')");
            table.HasCheckConstraint(
                "CK_recovery_reservations_cancelled_reason",
                "(status = 'cancelled' AND cancelled_reason IN ('student', 'shift_cancelled')) OR " +
                "(status <> 'cancelled' AND cancelled_reason IS NULL)");
            table.HasCheckConstraint(
                "CK_recovery_reservations_transition_audit",
                "(status = 'confirmed' AND attended_at IS NULL AND cancelled_at IS NULL AND cancelled_reason IS NULL) OR " +
                "(status = 'attended' AND attended_at IS NOT NULL AND cancelled_at IS NULL AND cancelled_reason IS NULL) OR " +
                "(status = 'cancelled' AND attended_at IS NULL AND cancelled_at IS NOT NULL AND cancelled_reason IS NOT NULL) OR " +
                "(status = 'missed' AND attended_at IS NULL AND cancelled_at IS NULL AND cancelled_reason IS NULL)");
        });
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.LocalDate).HasColumnType("date").IsRequired();
        builder.Property(reservation => reservation.Status)
            .HasConversion(value => ReservationStatusToDatabase(value), value => ReservationStatusFromDatabase(value))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(reservation => reservation.CancelledReason)
            .HasConversion(
                value => value == RecoveryReservationCancelReason.Student ? "student" :
                    value == RecoveryReservationCancelReason.ShiftCancelled ? "shift_cancelled" : null,
                value => value == "student" ? RecoveryReservationCancelReason.Student :
                    value == "shift_cancelled" ? RecoveryReservationCancelReason.ShiftCancelled : null)
            .HasMaxLength(30);
        builder.HasIndex(reservation => reservation.RecoveryCreditId)
            .IsUnique()
            .HasFilter("status = 'confirmed'");
        builder.HasIndex(reservation => new { reservation.StudentId, reservation.LocalDate })
            .IsUnique()
            .HasFilter("status = 'confirmed'");
        builder.HasOne<Student>().WithMany().HasForeignKey(reservation => reservation.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RecoveryCredit>().WithMany()
            .HasForeignKey(reservation => new { reservation.StudentId, reservation.RecoveryCreditId })
            .HasPrincipalKey(credit => new { credit.StudentId, credit.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GymShift>().WithMany().HasForeignKey(reservation => reservation.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string ReservationStatusToDatabase(RecoveryReservationStatus status) => status switch
    {
        RecoveryReservationStatus.Confirmed => "confirmed",
        RecoveryReservationStatus.Attended => "attended",
        RecoveryReservationStatus.Cancelled => "cancelled",
        RecoveryReservationStatus.Missed => "missed",
        _ => throw new InvalidOperationException("Recovery reservation status is invalid.")
    };

    private static RecoveryReservationStatus ReservationStatusFromDatabase(string status) => status switch
    {
        "confirmed" => RecoveryReservationStatus.Confirmed,
        "attended" => RecoveryReservationStatus.Attended,
        "cancelled" => RecoveryReservationStatus.Cancelled,
        "missed" => RecoveryReservationStatus.Missed,
        _ => throw new InvalidOperationException("Stored recovery reservation status is invalid.")
    };
}
