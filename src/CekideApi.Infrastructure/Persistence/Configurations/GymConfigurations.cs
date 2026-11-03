using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CekideApi.Infrastructure.Persistence.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(Plan.MaxNameLength).IsRequired();
        builder.Property(p => p.MonthlyPrice).HasPrecision(18, 2);
        builder.HasIndex(p => p.Name).IsUnique();
    }
}

public sealed class PassTariffConfiguration : IEntityTypeConfiguration<PassTariff>
{
    public void Configure(EntityTypeBuilder<PassTariff> builder)
    {
        builder.ToTable("pass_tariffs");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Price).HasPrecision(18, 2);
        builder.HasIndex(t => t.Type).IsUnique();
    }
}

public sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.FullName).HasMaxLength(Student.MaxNameLength).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(Student.MaxEmailLength).IsRequired();
        builder.Property(s => s.Phone).HasMaxLength(Student.MaxPhoneLength);
        builder.Property(s => s.IdentityId).HasMaxLength(Student.MaxIdentityIdLength);
        builder.Property(s => s.RoutineBlobPath).HasMaxLength(300);
        builder.Property(s => s.RoutineFileName).HasMaxLength(255);
        builder.Property(s => s.RoutineUploadedBy).HasMaxLength(100);

        builder.HasIndex(s => s.Email).IsUnique();
        builder.HasIndex(s => s.IdentityId);

        builder.HasOne<Plan>().WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany().HasForeignKey(s => s.PendingPlanId).OnDelete(DeleteBehavior.Restrict);
    }
}
