using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CekideApi.Infrastructure.Persistence.Configurations;

public sealed class GymShiftConfiguration : IEntityTypeConfiguration<GymShift>
{
    public void Configure(EntityTypeBuilder<GymShift> builder)
    {
        builder.ToTable("gym_shifts", table =>
        {
            table.HasCheckConstraint("CK_gym_shifts_capacity", "capacity = 12");
            table.HasCheckConstraint(
                "CK_gym_shifts_cancellation_audit",
                "(status = 'open' AND cancelled_by IS NULL AND cancelled_at IS NULL) OR " +
                "(status = 'cancelled' AND cancelled_by IS NOT NULL AND cancelled_at IS NOT NULL)");
            table.HasCheckConstraint("CK_gym_shifts_status", "status IN ('open', 'cancelled')");
        });
        builder.HasKey(shift => shift.Id);
        builder.Property(shift => shift.LocalDate).HasColumnType("date").IsRequired();
        builder.Property(shift => shift.StartsAt).HasColumnType("time(0)").IsRequired();
        builder.Property(shift => shift.EndsAt).HasColumnType("time(0)").IsRequired();
        builder.Property(shift => shift.Capacity).HasDefaultValue(GymShift.CapacityPerShift);
        builder.Property(shift => shift.Status)
            .HasConversion(status => status == GymShiftStatus.Open ? "open" : "cancelled",
                status => status == "open" ? GymShiftStatus.Open : GymShiftStatus.Cancelled)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(shift => shift.CreatedBy).HasMaxLength(GymShift.MaxActorLength).IsRequired();
        builder.Property(shift => shift.CancelledBy).HasMaxLength(GymShift.MaxActorLength);
        builder.HasIndex(shift => new { shift.LocalDate, shift.StartsAt }).IsUnique();
        builder.HasIndex(shift => shift.Status);
    }
}

public sealed class ShiftHabitualOccupancyConfiguration : IEntityTypeConfiguration<ShiftHabitualOccupancy>
{
    public void Configure(EntityTypeBuilder<ShiftHabitualOccupancy> builder)
    {
        builder.ToTable("shift_habitual_occupancies", table =>
        {
            table.HasCheckConstraint("CK_shift_habitual_occupancies_status", "status IN ('active', 'removed')");
            table.HasCheckConstraint(
                "CK_shift_habitual_occupancies_removal_audit",
                "(status = 'active' AND removed_by IS NULL AND removed_at IS NULL) OR " +
                "(status = 'removed' AND removed_by IS NOT NULL AND removed_at IS NOT NULL)");
        });
        builder.HasKey(occupancy => occupancy.Id);
        builder.Property(occupancy => occupancy.LocalDate).HasColumnType("date").IsRequired();
        builder.Property(occupancy => occupancy.Status)
            .HasConversion(status => status == ShiftHabitualOccupancyStatus.Active ? "active" : "removed",
                status => status == "active" ? ShiftHabitualOccupancyStatus.Active : ShiftHabitualOccupancyStatus.Removed)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(occupancy => occupancy.RegisteredBy).HasMaxLength(ShiftHabitualOccupancy.MaxActorLength).IsRequired();
        builder.Property(occupancy => occupancy.RemovedBy).HasMaxLength(ShiftHabitualOccupancy.MaxActorLength);
        builder.HasIndex(occupancy => new { occupancy.ShiftId, occupancy.StudentId })
            .IsUnique()
            .HasFilter("status = 'active'");
        builder.HasIndex(occupancy => new { occupancy.StudentId, occupancy.LocalDate })
            .IsUnique()
            .HasFilter("status = 'active'");
        builder.HasOne<GymShift>().WithMany().HasForeignKey(occupancy => occupancy.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Student>().WithMany().HasForeignKey(occupancy => occupancy.StudentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
