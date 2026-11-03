using CekideApi.Domain;
using CekideApi.Infrastructure.Persistence;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public sealed class OverdueNotificationPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new();

    public OverdueNotificationPersistenceTests()
    {
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CekideDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CekideDbContext>().UseSqlite(_connection).Options, _clock);

    private async Task<int> AddStudentAsync(string email = "ana@example.com")
    {
        await using var context = NewContext();
        var student = Student.Create("Ana Pérez", email, null, null, new DateOnly(2026, 10, 1));
        context.Students.Add(student);
        await context.SaveChangesAsync();
        return student.Id;
    }

    [Fact]
    public async Task A_notification_is_stored_with_snake_case_columns_and_its_state()
    {
        var studentId = await AddStudentAsync();
        await using (var context = NewContext())
        {
            var notification = OverdueNotification.Create(studentId, PaymentPeriod.Parse("2026-11"));
            notification.MarkFailed("Brevo error 500");
            context.OverdueNotifications.Add(notification);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var stored = await context.OverdueNotifications.SingleAsync();
            Assert.Equal(studentId, stored.StudentId);
            Assert.Equal("2026-11", stored.Period);
            Assert.Equal(NotificationStatus.Failed, stored.Status);
            Assert.Equal(1, stored.Attempts);
            Assert.Equal("Brevo error 500", stored.LastError);
            Assert.Null(stored.SentAt);
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(overdue_notifications)";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        foreach (var column in new[] { "id", "student_id", "period", "status", "attempts", "sent_at", "last_error", "created_at", "updated_at" })
        {
            Assert.Contains(column, columns);
        }
    }

    [Fact]
    public async Task The_unique_index_rejects_a_second_notification_for_the_same_student_and_period()
    {
        var studentId = await AddStudentAsync();
        await using var context = NewContext();
        context.OverdueNotifications.Add(OverdueNotification.Create(studentId, PaymentPeriod.Parse("2026-11")));
        await context.SaveChangesAsync();

        await using var other = NewContext();
        other.OverdueNotifications.Add(OverdueNotification.Create(studentId, PaymentPeriod.Parse("2026-11")));

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task The_same_student_can_have_one_notification_per_period_and_other_students_the_same_period()
    {
        var ana = await AddStudentAsync("ana@example.com");
        var beto = await AddStudentAsync("beto@example.com");
        await using var context = NewContext();

        context.OverdueNotifications.AddRange(
            OverdueNotification.Create(ana, PaymentPeriod.Parse("2026-11")),
            OverdueNotification.Create(ana, PaymentPeriod.Parse("2026-12")),
            OverdueNotification.Create(beto, PaymentPeriod.Parse("2026-11")));
        await context.SaveChangesAsync();

        Assert.Equal(3, await context.OverdueNotifications.CountAsync());
    }

    [Fact]
    public async Task The_unit_of_work_reports_the_duplicate_as_a_conflict()
    {
        var studentId = await AddStudentAsync();
        await using (var context = NewContext())
        {
            context.OverdueNotifications.Add(OverdueNotification.Create(studentId, PaymentPeriod.Parse("2026-11")));
            await context.SaveChangesAsync();
        }

        await using var other = NewContext();
        other.OverdueNotifications.Add(OverdueNotification.Create(studentId, PaymentPeriod.Parse("2026-11")));

        await Assert.ThrowsAsync<ConflictException>(() => new UnitOfWork(other).SaveChangesAsync());
    }

    [Fact]
    public async Task A_notification_requires_an_existing_student()
    {
        await using var context = NewContext();
        context.OverdueNotifications.Add(OverdueNotification.Create(999, PaymentPeriod.Parse("2026-11")));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
