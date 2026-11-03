using CekideApi.Domain;

namespace CekideApi.Application.Notifications;

/// <param name="Status">sent | failed</param>
public sealed record NotificationDto(
    int Id,
    int StudentId,
    string? StudentName,
    string? StudentEmail,
    string Period,
    NotificationStatus Status,
    int Attempts,
    DateTime? SentAt,
    string? LastError)
{
    public static NotificationDto From(OverdueNotification n, Student? student) =>
        new(n.Id, n.StudentId, student?.FullName, student?.Email, n.Period, n.Status, n.Attempts, n.SentAt, n.LastError);
}

public sealed record OverdueRunResult(int Sent, int Failed);
