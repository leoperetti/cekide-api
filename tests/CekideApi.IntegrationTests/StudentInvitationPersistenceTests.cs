using CekideApi.Domain;
using CekideApi.Infrastructure.Persistence;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public sealed class StudentInvitationPersistenceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new();

    public StudentInvitationPersistenceTests()
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

    private string Scalar(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    [Fact]
    public async Task An_invitation_is_stored_with_snake_case_columns()
    {
        var studentId = await AddStudentAsync();
        await using (var context = NewContext())
        {
            context.StudentInvitations.Add(StudentInvitation.Send(studentId, "ana@example.com", 41, Now.AddDays(7), "teacher-1", Now));
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var stored = await context.StudentInvitations.SingleAsync();
            Assert.Equal(studentId, stored.StudentId);
            Assert.Equal("ana@example.com", stored.Email);
            Assert.Equal(41, stored.ExternalInvitationId);
            Assert.Equal(Now.AddDays(7), stored.ExpiresAt);
            Assert.Equal("teacher-1", stored.SentBy);
            Assert.Equal(Now, stored.SentAt);
            Assert.Equal(InvitationStatus.Sent, stored.Status);
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(student_invitations)";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        foreach (var column in new[]
                 {
                     "id", "student_id", "email", "external_invitation_id", "sent_at", "expires_at", "sent_by", "status",
                     "created_at", "updated_at"
                 })
        {
            Assert.Contains(column, columns);
        }
    }

    [Theory]
    [InlineData(InvitationStatus.Sent, "Sent")]
    [InlineData(InvitationStatus.Accepted, "Accepted")]
    [InlineData(InvitationStatus.Expired, "Expired")]
    public async Task The_status_is_stored_as_text(InvitationStatus status, string expected)
    {
        var studentId = await AddStudentAsync();
        await using (var context = NewContext())
        {
            var invitation = StudentInvitation.Send(studentId, "ana@example.com", 41, Now.AddDays(7), "teacher-1", Now);
            if (status == InvitationStatus.Accepted)
            {
                invitation.MarkAccepted();
            }
            else if (status == InvitationStatus.Expired)
            {
                invitation.MarkExpired();
            }

            context.StudentInvitations.Add(invitation);
            await context.SaveChangesAsync();
        }

        Assert.Equal(expected, Scalar("SELECT status FROM student_invitations"));
    }

    [Fact]
    public async Task A_student_can_have_several_invitations()
    {
        var studentId = await AddStudentAsync();
        await using var context = NewContext();

        context.StudentInvitations.AddRange(
            StudentInvitation.Send(studentId, "ana@example.com", 41, Now.AddDays(7), "teacher-1", Now),
            StudentInvitation.Send(studentId, "ana@example.com", 42, Now.AddDays(7), "teacher-1", Now));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.StudentInvitations.CountAsync(i => i.StudentId == studentId));
    }

    [Fact]
    public async Task An_invitation_requires_an_existing_student()
    {
        await using var context = NewContext();
        context.StudentInvitations.Add(StudentInvitation.Send(999, "ana@example.com", 41, Now.AddDays(7), "teacher-1", Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_student_with_invitations_cannot_be_deleted()
    {
        var studentId = await AddStudentAsync();
        await using (var context = NewContext())
        {
            context.StudentInvitations.Add(StudentInvitation.Send(studentId, "ana@example.com", 41, Now.AddDays(7), "teacher-1", Now));
            await context.SaveChangesAsync();
        }

        await using var other = NewContext();
        other.Students.Remove(await other.Students.SingleAsync());

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }
}
