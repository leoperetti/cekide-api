using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CekideApi.Infrastructure.Persistence.Configurations;

public sealed class OverdueNotificationConfiguration : IEntityTypeConfiguration<OverdueNotification>
{
    public void Configure(EntityTypeBuilder<OverdueNotification> builder)
    {
        builder.ToTable("overdue_notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Period).HasMaxLength(7).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(n => n.LastError).HasMaxLength(OverdueNotification.MaxErrorLength);

        builder.HasOne<Student>().WithMany().HasForeignKey(n => n.StudentId).OnDelete(DeleteBehavior.Restrict);

        // One notice per student and month, even across restarts or several instances.
        builder.HasIndex(n => new { n.StudentId, n.Period }).IsUnique();
        builder.HasIndex(n => n.Period);
    }
}

public sealed class StudentInvitationConfiguration : IEntityTypeConfiguration<StudentInvitation>
{
    public void Configure(EntityTypeBuilder<StudentInvitation> builder)
    {
        builder.ToTable("student_invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Email).HasMaxLength(StudentInvitation.MaxEmailLength).IsRequired();
        builder.Property(i => i.SentBy).HasMaxLength(StudentInvitation.MaxSentByLength).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(i => i.FirstName).HasMaxLength(Student.MaxNameLength);
        builder.Property(i => i.LastName).HasMaxLength(Student.MaxNameLength);
        builder.Property(i => i.FullName).HasMaxLength(Student.MaxNameLength);
        builder.Property(i => i.PhoneNumber).HasMaxLength(Student.MaxPhoneLength);
        builder.Property(i => i.Dni).HasMaxLength(Student.MaxDniLength);
        builder.Property(i => i.RequiresRegistrationProfile).HasDefaultValue(false);
        builder.Ignore(i => i.RegistrationProfile);

        builder.HasOne<Student>().WithMany().HasForeignKey(i => i.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.StudentId);
    }
}
