namespace CekideApi.Domain;

public enum NotificationStatus
{
    Sent,
    Failed
}

/// <summary>
/// The single overdue notice of a student for a billing period (unique per student and period).
/// A row is created as <see cref="NotificationStatus.Failed"/> with zero attempts before the email is
/// sent, so concurrent runs cannot send the same notice twice; every attempt then updates it.
/// </summary>
public sealed class OverdueNotification : Entity
{
    public const int MaxErrorLength = 500;

    private OverdueNotification()
    {
    }

    public int StudentId { get; private set; }

    /// <summary>Billing month as YYYY-MM.</summary>
    public string Period { get; private set; } = string.Empty;

    public NotificationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? SentAt { get; private set; }

    public string? LastError { get; private set; }

    public static OverdueNotification Create(int studentId, PaymentPeriod period) =>
        new() { StudentId = studentId, Period = period.ToString(), Status = NotificationStatus.Failed };

    public void MarkSent(DateTime sentAtUtc)
    {
        Attempts++;
        Status = NotificationStatus.Sent;
        SentAt = sentAtUtc;
        LastError = null;
    }

    public void MarkFailed(string? error)
    {
        Attempts++;
        Status = NotificationStatus.Failed;
        LastError = Truncate(string.IsNullOrWhiteSpace(error) ? "Unknown error." : error.Trim());
    }

    private static string Truncate(string value) =>
        value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}
