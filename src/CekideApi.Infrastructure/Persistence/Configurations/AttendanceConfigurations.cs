using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CekideApi.Infrastructure.Persistence.Configurations;

public sealed class AttendancePeriodConfiguration : IEntityTypeConfiguration<AttendancePeriod>
{
    public void Configure(EntityTypeBuilder<AttendancePeriod> builder)
    {
        builder.ToTable("attendance_periods", table =>
        {
            table.HasCheckConstraint(
                "CK_attendance_periods_weekly_limit",
                "weekly_limit BETWEEN 1 AND 7");
            table.HasCheckConstraint(
                "CK_attendance_periods_monthly_limit",
                "monthly_limit > 0");
        });
        builder.HasKey(period => period.Id);
        builder.Property(period => period.Period).HasColumnType("char(7)").IsRequired();
        builder.Property(period => period.PlanName)
            .HasMaxLength(AttendancePeriod.MaxPlanNameLength)
            .IsRequired();
        builder.HasIndex(period => new { period.StudentId, period.Period }).IsUnique();

        builder.HasOne<Student>().WithMany()
            .HasForeignKey(period => period.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany()
            .HasForeignKey(period => period.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlanAttendanceLimits>().WithMany()
            .HasForeignKey(period => new { period.PlanId, period.PlanLimitsId })
            .HasPrincipalKey(limits => new { limits.PlanId, limits.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
        builder.ToTable("attendances", table =>
        {
            table.HasCheckConstraint(
                "CK_attendances_source_kiosk",
                "(source = 'kiosk' AND kiosk_session_id IS NOT NULL) OR (source = 'self' AND kiosk_session_id IS NULL)");
            table.HasCheckConstraint(
                "CK_attendances_charge_recovery",
                "(charge_kind = 'ordinary' AND recovery_credit_id IS NULL) OR " +
                "(charge_kind = 'recovery' AND recovery_credit_id IS NOT NULL)");
        });
        builder.HasKey(attendance => attendance.Id);
        builder.Property(attendance => attendance.LocalDate).HasColumnType("date");
        builder.Property(attendance => attendance.CheckedInAt).HasColumnType("datetime2");
        builder.Property(attendance => attendance.Source)
            .HasConversion(source => AttendanceSourceToDatabase(source), source => AttendanceSourceFromDatabase(source))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(attendance => attendance.ChargeKind)
            .HasConversion(
                chargeKind => AttendanceChargeKindToDatabase(chargeKind),
                chargeKind => AttendanceChargeKindFromDatabase(chargeKind))
            .HasMaxLength(20)
            .IsRequired();
        builder.HasIndex(attendance => new { attendance.StudentId, attendance.LocalDate }).IsUnique();
        builder.HasIndex(attendance => attendance.RecoveryCreditId)
            .IsUnique()
            .HasFilter("recovery_credit_id IS NOT NULL");

        builder.HasOne<AttendancePeriod>().WithMany()
            .HasForeignKey(attendance => new { attendance.StudentId, attendance.AttendancePeriodId })
            .HasPrincipalKey(period => new { period.StudentId, period.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<KioskSession>().WithMany()
            .HasForeignKey(attendance => attendance.KioskSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GymShift>().WithMany()
            .HasForeignKey(attendance => attendance.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RecoveryCredit>().WithMany()
            .HasForeignKey(attendance => attendance.RecoveryCreditId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string AttendanceSourceToDatabase(AttendanceSource source) => source switch
    {
        AttendanceSource.Self => "self",
        AttendanceSource.Kiosk => "kiosk",
        _ => throw new InvalidOperationException("Attendance source is invalid.")
    };

    private static AttendanceSource AttendanceSourceFromDatabase(string source) => source switch
    {
        "self" => AttendanceSource.Self,
        "kiosk" => AttendanceSource.Kiosk,
        _ => throw new InvalidOperationException("Stored attendance source is invalid.")
    };

    private static string AttendanceChargeKindToDatabase(AttendanceChargeKind chargeKind) => chargeKind switch
    {
        AttendanceChargeKind.Ordinary => "ordinary",
        AttendanceChargeKind.Recovery => "recovery",
        _ => throw new InvalidOperationException("Attendance charge kind is invalid.")
    };

    private static AttendanceChargeKind AttendanceChargeKindFromDatabase(string chargeKind) => chargeKind switch
    {
        "ordinary" => AttendanceChargeKind.Ordinary,
        "recovery" => AttendanceChargeKind.Recovery,
        _ => throw new InvalidOperationException("Stored attendance charge kind is invalid.")
    };
}

public sealed class KioskSessionConfiguration : IEntityTypeConfiguration<KioskSession>
{
    public void Configure(EntityTypeBuilder<KioskSession> builder)
    {
        builder.ToTable("kiosk_sessions", table =>
        {
            table.HasCheckConstraint(
                "CK_kiosk_sessions_activation_expiry",
                "activation_expires_at > created_at");
            table.HasCheckConstraint(
                "CK_kiosk_sessions_activation_state",
                "(activated_at IS NULL AND token_hash IS NULL AND expires_at IS NULL) OR " +
                "(activated_at IS NOT NULL AND token_hash IS NOT NULL AND expires_at > activated_at)");
        });
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Label).HasMaxLength(KioskSession.MaxLabelLength).IsRequired();
        builder.Property(session => session.Tenant).HasMaxLength(KioskSession.MaxTenantLength).IsRequired();
        builder.Property(session => session.CreatedBy).HasMaxLength(KioskSession.MaxCreatedByLength).IsRequired();
        builder.Property(session => session.ActivationCodeHash).HasMaxLength(128);
        builder.Property(session => session.TokenHash).HasMaxLength(128);
        builder.Property(session => session.RevokedBy).HasMaxLength(KioskSession.MaxRevokedByLength);
        builder.Property(session => session.RowVersion).IsRowVersion();
        builder.HasIndex(session => session.ActivationCodeHash).IsUnique().HasFilter("activation_code_hash IS NOT NULL");
        builder.HasIndex(session => session.TokenHash).IsUnique().HasFilter("token_hash IS NOT NULL");
        builder.HasIndex(session => new { session.Tenant, session.CreatedAt });
    }
}

public sealed class AttendanceQuotaNotificationConfiguration : IEntityTypeConfiguration<AttendanceQuotaNotification>
{
    public void Configure(EntityTypeBuilder<AttendanceQuotaNotification> builder)
    {
        builder.ToTable("attendance_quota_notifications", table =>
        {
            table.HasCheckConstraint(
                "CK_attendance_quota_notifications_monthly_limit",
                "monthly_limit > 0");
            table.HasCheckConstraint(
                "CK_attendance_quota_notifications_attempts",
                "attempts >= 0");
        });
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Period)
            .HasColumnType("char(7)")
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();
        builder.Property(notification => notification.RecipientEmail)
            .HasMaxLength(Student.MaxEmailLength)
            .IsRequired();
        builder.Property(notification => notification.RecipientName)
            .HasMaxLength(AttendanceQuotaNotification.MaxRecipientNameLength)
            .IsRequired();
        builder.Property(notification => notification.PlanName)
            .HasMaxLength(AttendancePeriod.MaxPlanNameLength)
            .IsRequired();
        builder.Property(notification => notification.RecommendedPlanName)
            .HasMaxLength(AttendancePeriod.MaxPlanNameLength);
        builder.Property(notification => notification.RecommendedPrice).HasPrecision(18, 2);
        builder.Property(notification => notification.Status)
            .HasConversion(
                status => AttendanceQuotaNotificationStatusToDatabase(status),
                status => AttendanceQuotaNotificationStatusFromDatabase(status))
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(notification => notification.ClaimId)
            .HasMaxLength(AttendanceQuotaNotification.MaxClaimIdLength);
        builder.Property(notification => notification.LastError)
            .HasMaxLength(AttendanceQuotaNotification.MaxErrorLength);
        builder.Property(notification => notification.ResolvedBy)
            .HasMaxLength(KioskSession.MaxRevokedByLength);
        builder.Property(notification => notification.ResolutionEvidence)
            .HasMaxLength(AttendanceQuotaNotification.MaxResolutionEvidenceLength);
        builder.HasIndex(notification => new { notification.StudentId, notification.Period }).IsUnique();
        builder.HasIndex(notification => new { notification.Status, notification.NextAttemptAt });

        builder.HasOne<Student>().WithMany()
            .HasForeignKey(notification => notification.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttendancePeriod>().WithMany()
            .HasForeignKey(notification => new { notification.StudentId, notification.AttendancePeriodId })
            .HasPrincipalKey(period => new { period.StudentId, period.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static string AttendanceQuotaNotificationStatusToDatabase(AttendanceQuotaNotificationStatus status) => status switch
    {
        AttendanceQuotaNotificationStatus.Pending => "pending",
        AttendanceQuotaNotificationStatus.Sending => "sending",
        AttendanceQuotaNotificationStatus.Sent => "sent",
        AttendanceQuotaNotificationStatus.Failed => "failed",
        AttendanceQuotaNotificationStatus.DeliveryUnknown => "delivery_unknown",
        _ => throw new InvalidOperationException("Attendance quota notification status is invalid.")
    };

    private static AttendanceQuotaNotificationStatus AttendanceQuotaNotificationStatusFromDatabase(string status) => status switch
    {
        "pending" => AttendanceQuotaNotificationStatus.Pending,
        "sending" => AttendanceQuotaNotificationStatus.Sending,
        "sent" => AttendanceQuotaNotificationStatus.Sent,
        "failed" => AttendanceQuotaNotificationStatus.Failed,
        "delivery_unknown" => AttendanceQuotaNotificationStatus.DeliveryUnknown,
        _ => throw new InvalidOperationException("Stored attendance quota notification status is invalid.")
    };
}
