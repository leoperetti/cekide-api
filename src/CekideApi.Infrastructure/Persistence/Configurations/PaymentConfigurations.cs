using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CekideApi.Infrastructure.Persistence.Configurations;

public sealed class MonthlyPaymentConfiguration : IEntityTypeConfiguration<MonthlyPayment>
{
    public void Configure(EntityTypeBuilder<MonthlyPayment> builder)
    {
        builder.ToTable("monthly_payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Period).HasMaxLength(7).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.RegisteredBy).HasMaxLength(100).IsRequired();

        builder.HasOne<Student>().WithMany().HasForeignKey(p => p.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.Period);

        // A student can have only one Paid payment per month; voided ones do not count.
        builder.HasIndex(p => new { p.StudentId, p.Period })
            .IsUnique()
            .HasFilter("[status] = 'Paid'");
    }
}

public sealed class ShortTermPassConfiguration : IEntityTypeConfiguration<ShortTermPass>
{
    public void Configure(EntityTypeBuilder<ShortTermPass> builder)
    {
        builder.ToTable("short_term_passes");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.RegisteredBy).HasMaxLength(100).IsRequired();

        builder.HasOne<Student>().WithMany().HasForeignKey(p => p.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.StudentId);
    }
}

public sealed class PaymentHistoryConfiguration : IEntityTypeConfiguration<PaymentHistory>
{
    public void Configure(EntityTypeBuilder<PaymentHistory> builder)
    {
        builder.ToTable("payment_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.PaymentKind).HasConversion<string>().HasMaxLength(10);
        builder.Property(h => h.Action).HasConversion<string>().HasMaxLength(10);
        builder.Property(h => h.PerformedBy).HasMaxLength(100).IsRequired();

        // payment_id points to monthly_payments or short_term_passes depending on payment_kind, so there is no FK.
        builder.HasIndex(h => new { h.PaymentKind, h.PaymentId });
    }
}
