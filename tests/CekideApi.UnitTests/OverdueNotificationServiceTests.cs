using CekideApi.Application.Notifications;
using CekideApi.Domain;
using CekideApi.UnitTests.Support;

namespace CekideApi.UnitTests;

public class OverdueNotificationServiceTests
{
    private readonly TestClock _clock = new();
    private readonly InMemoryStudentRepository _students = new();
    private readonly InMemoryPaymentRepository _payments = new();
    private readonly InMemoryOverdueNotificationRepository _notifications = new();
    private readonly FakeEmailSender _email = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private static readonly DateOnly October1 = new(2026, 10, 1);

    public OverdueNotificationServiceTests() => _clock.SetBuenosAiresDate(2026, 11, 12);

    private OverdueNotificationService CreateService() =>
        new(_students, _payments, _notifications, _email, _unitOfWork, _clock);

    private Student MonthlyStudent(string name = "Ana Pérez", string email = "ana@example.com", DateOnly? startedOn = null) =>
        _students.Seed(name, email, planId: 1, startedOn ?? October1);

    [Fact]
    public async Task Sends_only_to_active_students_with_an_unpaid_monthly_membership()
    {
        var unpaid = MonthlyStudent("Ana Pérez", "ana@example.com");
        var paid = MonthlyStudent("Beto Paz", "beto@example.com");
        _payments.SeedPaid(paid, "2026-11");
        _students.Seed("Carla Pase", "carla@example.com", planId: null, October1);
        _students.Seed("Dario Baja", "dario@example.com", planId: 1, October1, active: false);

        var result = await CreateService().ProcessAsync();

        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Failed);
        var message = Assert.Single(_email.Sent);
        Assert.Equal("ana@example.com", message.ToEmail);
        var notification = Assert.Single(_notifications.Items);
        Assert.Equal(unpaid.Id, notification.StudentId);
        Assert.Equal("2026-11", notification.Period);
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(1, notification.Attempts);
        Assert.Equal(_clock.UtcNow.UtcDateTime, notification.SentAt);
        Assert.Null(notification.LastError);
    }

    [Fact]
    public async Task Does_not_notify_before_the_overdue_day()
    {
        MonthlyStudent();
        _clock.SetBuenosAiresDate(2026, 11, 10);

        var result = await CreateService().ProcessAsync();

        Assert.Equal(0, result.Sent);
        Assert.Empty(_email.Sent);
        Assert.Empty(_notifications.Items);
    }

    [Fact]
    public async Task Does_not_notify_a_student_who_joined_the_monthly_membership_after_day_10_of_the_month()
    {
        _clock.SetBuenosAiresDate(2026, 11, 20);
        MonthlyStudent(startedOn: new DateOnly(2026, 11, 12));

        await CreateService().ProcessAsync();

        Assert.Empty(_email.Sent);
        Assert.Empty(_notifications.Items);
    }

    [Fact]
    public async Task A_second_run_does_not_send_a_duplicate_notice_for_the_same_student_and_period()
    {
        MonthlyStudent();
        var service = CreateService();

        await service.ProcessAsync();
        var second = await service.ProcessAsync();

        Assert.Equal(0, second.Sent);
        Assert.Single(_email.Sent);
        Assert.Single(_notifications.Items);
    }

    [Fact]
    public async Task A_new_period_gets_its_own_notice()
    {
        MonthlyStudent();
        var service = CreateService();
        await service.ProcessAsync();

        _clock.SetBuenosAiresDate(2026, 12, 15);
        await service.ProcessAsync();

        Assert.Equal(2, _email.Sent.Count);
        Assert.Equal(["2026-11", "2026-12"], _notifications.Items.Select(n => n.Period).OrderBy(p => p));
    }

    [Fact]
    public async Task A_failed_notice_is_recorded_and_retried_on_the_next_run()
    {
        MonthlyStudent();
        _email.FailWith = "Brevo error 500";
        var service = CreateService();

        var first = await service.ProcessAsync();

        Assert.Equal(1, first.Failed);
        var notification = Assert.Single(_notifications.Items);
        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.Attempts);
        Assert.Equal("Brevo error 500", notification.LastError);
        Assert.Null(notification.SentAt);

        _email.FailWith = null;
        var second = await service.ProcessAsync();

        Assert.Equal(1, second.Sent);
        Assert.Single(_notifications.Items);
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(2, notification.Attempts);
        Assert.Null(notification.LastError);
        Assert.NotNull(notification.SentAt);

        await service.ProcessAsync();
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task An_unexpected_sender_exception_is_recorded_as_failed_and_does_not_stop_other_students()
    {
        MonthlyStudent("Ana Pérez", "ana@example.com");
        MonthlyStudent("Beto Paz", "beto@example.com");
        _email.ThrowWith = new InvalidOperationException("boom");

        var result = await CreateService().ProcessAsync();

        Assert.Equal(2, result.Failed);
        Assert.All(_notifications.Items, n =>
        {
            Assert.Equal(NotificationStatus.Failed, n.Status);
            Assert.Equal("boom", n.LastError);
        });
    }

    [Fact]
    public async Task A_later_payment_stops_the_notices_for_a_failed_one()
    {
        var student = MonthlyStudent();
        _email.FailWith = "Brevo error 500";
        var service = CreateService();
        await service.ProcessAsync();
        _email.FailWith = null;

        _payments.SeedPaid(student, "2026-11");
        var result = await service.ProcessAsync();

        Assert.Equal(0, result.Sent);
        Assert.Empty(_email.Sent);
        Assert.Equal(1, Assert.Single(_notifications.Items).Attempts);
    }

    [Fact]
    public async Task The_email_is_written_in_spanish_for_the_student_and_the_month()
    {
        MonthlyStudent("Ana Pérez & Co", "ana@example.com");

        await CreateService().ProcessAsync();

        var message = Assert.Single(_email.Sent);
        Assert.Equal("Ana Pérez & Co", message.ToName);
        Assert.Contains("cuota", message.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("noviembre de 2026", message.Subject);
        Assert.Contains("Hola Ana Pérez &amp; Co", message.HtmlBody);
        Assert.Contains("noviembre de 2026", message.HtmlBody);
    }

    [Fact]
    public async Task Retry_sends_a_failed_notice_and_marks_it_sent()
    {
        MonthlyStudent();
        _email.FailWith = "Brevo error 500";
        var service = CreateService();
        await service.ProcessAsync();
        var id = _notifications.Items.Single().Id;
        _email.FailWith = null;

        var dto = await service.RetryAsync(id);

        Assert.Equal(NotificationStatus.Sent, dto.Status);
        Assert.Equal(2, dto.Attempts);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task Retry_of_a_failed_notice_that_fails_again_keeps_it_failed()
    {
        MonthlyStudent();
        _email.FailWith = "Brevo error 500";
        var service = CreateService();
        await service.ProcessAsync();
        var id = _notifications.Items.Single().Id;
        _email.FailWith = "still down";

        var dto = await service.RetryAsync(id);

        Assert.Equal(NotificationStatus.Failed, dto.Status);
        Assert.Equal(2, dto.Attempts);
        Assert.Equal("still down", dto.LastError);
    }

    [Fact]
    public async Task Retry_of_a_sent_notice_is_a_conflict()
    {
        MonthlyStudent();
        var service = CreateService();
        await service.ProcessAsync();

        await Assert.ThrowsAsync<ConflictException>(() => service.RetryAsync(_notifications.Items.Single().Id));
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task Retry_of_a_missing_notice_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => CreateService().RetryAsync(999));
    }

    [Fact]
    public async Task Retry_is_refused_once_the_student_has_paid_the_period()
    {
        var student = MonthlyStudent();
        _email.FailWith = "Brevo error 500";
        var service = CreateService();
        await service.ProcessAsync();
        _payments.SeedPaid(student, "2026-11");
        _email.FailWith = null;

        await Assert.ThrowsAsync<ConflictException>(() => service.RetryAsync(_notifications.Items.Single().Id));
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task List_returns_student_data_and_filters_by_period_and_status()
    {
        MonthlyStudent("Ana Pérez", "ana@example.com");
        _email.FailWith = "Brevo error 500";
        var service = CreateService();
        await service.ProcessAsync();

        var failed = await service.ListAsync(null, NotificationStatus.Failed);
        var sent = await service.ListAsync(null, NotificationStatus.Sent);
        var otherPeriod = await service.ListAsync("2026-10", null);

        var item = Assert.Single(failed);
        Assert.Equal("Ana Pérez", item.StudentName);
        Assert.Equal("ana@example.com", item.StudentEmail);
        Assert.Equal("2026-11", item.Period);
        Assert.Equal("Brevo error 500", item.LastError);
        Assert.Empty(sent);
        Assert.Empty(otherPeriod);
        await Assert.ThrowsAsync<DomainValidationException>(() => service.ListAsync("11-2026", null));
    }
}
