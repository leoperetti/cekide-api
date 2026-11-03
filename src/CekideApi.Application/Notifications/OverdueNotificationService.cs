using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Notifications;

/// <summary>
/// Sends one Spanish overdue notice per student and month to active students on a monthly membership
/// that have not paid; failed notices are retried on the next run and a payment stops them.
/// </summary>
public sealed class OverdueNotificationService(
    IStudentRepository students,
    IPaymentRepository payments,
    IOverdueNotificationRepository notifications,
    IEmailSender email,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly string[] MonthNames =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    ];

    public async Task<OverdueRunResult> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var today = GymCalendar.Today(clock.UtcNow);
        var period = PaymentPeriod.From(today);

        var paidStudentIds = await payments.PaidStudentIdsAsync(period.ToString(), cancellationToken);
        var existing = (await notifications.ListByPeriodAsync(period.ToString(), cancellationToken))
            .ToDictionary(n => n.StudentId);

        int sent = 0, failed = 0;
        foreach (var student in await students.SearchAsync(null, true, cancellationToken))
        {
            if (paidStudentIds.Contains(student.Id) || !IsOverdueWithNotice(student, period, today))
            {
                continue;
            }

            OverdueNotification notification;
            if (existing.TryGetValue(student.Id, out var previous))
            {
                if (previous.Status == NotificationStatus.Sent)
                {
                    continue;
                }

                notification = previous;
            }
            else if (await TryClaimAsync(student.Id, period, cancellationToken) is { } claimed)
            {
                notification = claimed;
            }
            else
            {
                continue;
            }

            if (await AttemptAsync(notification, student, period, cancellationToken))
            {
                sent++;
            }
            else
            {
                failed++;
            }
        }

        return new OverdueRunResult(sent, failed);
    }

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(
        string? period, NotificationStatus? status, CancellationToken cancellationToken = default)
    {
        var normalizedPeriod = period is null ? null : PaymentPeriod.Parse(period).ToString();
        var found = await notifications.ListAsync(normalizedPeriod, status, cancellationToken);
        var owners = (await students.GetByIdsAsync(found.Select(n => n.StudentId), cancellationToken))
            .ToDictionary(s => s.Id);

        return found
            .OrderByDescending(n => n.Period, StringComparer.Ordinal).ThenByDescending(n => n.Id)
            .Select(n => NotificationDto.From(n, owners.GetValueOrDefault(n.StudentId)))
            .ToList();
    }

    public async Task<NotificationDto> RetryAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new NotFoundException($"Notification {notificationId} was not found.");

        if (notification.Status == NotificationStatus.Sent)
        {
            throw new ConflictException("The notification was already sent.");
        }

        var student = await students.GetByIdAsync(notification.StudentId, cancellationToken)
                      ?? throw new NotFoundException($"Student {notification.StudentId} was not found.");

        if (!student.IsActive || await payments.PaidMonthlyExistsAsync(student.Id, notification.Period, cancellationToken))
        {
            throw new ConflictException("The student no longer owes this period, so the notice is not sent.");
        }

        await AttemptAsync(notification, student, PaymentPeriod.Parse(notification.Period), cancellationToken);
        return NotificationDto.From(notification, student);
    }

    private static bool IsOverdueWithNotice(Student student, PaymentPeriod period, DateOnly today) =>
        PaymentRules.ObligationFor(student, period) is { OverdueNoticeAllowed: true } obligation
        && PaymentRules.StatusOf(obligation, paid: false, today) == MonthlyPaymentStatus.Overdue;

    /// <summary>Inserts the (student, period) row first; losing the unique-index race means another run owns the notice.</summary>
    private async Task<OverdueNotification?> TryClaimAsync(int studentId, PaymentPeriod period, CancellationToken cancellationToken)
    {
        var notification = OverdueNotification.Create(studentId, period);
        try
        {
            notifications.Add(notification);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return notification;
        }
        catch (ConflictException)
        {
            notifications.Discard(notification);
            return null;
        }
    }

    private async Task<bool> AttemptAsync(
        OverdueNotification notification, Student student, PaymentPeriod period, CancellationToken cancellationToken)
    {
        EmailSendResult result;
        try
        {
            result = await email.SendAsync(BuildMessage(student, period), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = EmailSendResult.Fail(exception.Message);
        }

        if (result.Succeeded)
        {
            notification.MarkSent(clock.UtcNow.UtcDateTime);
        }
        else
        {
            notification.MarkFailed(result.Error);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Succeeded;
    }

    private static EmailMessage BuildMessage(Student student, PaymentPeriod period)
    {
        var month = $"{MonthNames[period.Month - 1]} de {period.Year}";
        var body =
            $"<p>Hola {Encode(student.FullName)},</p>" +
            $"<p>Te escribimos desde el gimnasio para avisarte que la cuota mensual de {month} figura como impaga.</p>" +
            "<p>Podés abonarla en el gimnasio a la brevedad. Si ya realizaste el pago, ignorá este mensaje.</p>" +
            "<p>¡Gracias!</p>";

        return new EmailMessage(student.Email, student.FullName, $"Cuota pendiente de {month}", body);
    }

    private static string Encode(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
