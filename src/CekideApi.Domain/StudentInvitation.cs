namespace CekideApi.Domain;

public enum InvitationStatus
{
    Sent,
    Accepted,
    Expired
}

/// <summary>
/// Reference to an invitation issued by authcraft-api for a student. The invitation code and its
/// validity live in authcraft-api; this row tracks who was invited, when, and how it ended.
/// </summary>
public sealed class StudentInvitation : Entity
{
    public const int MaxEmailLength = Student.MaxEmailLength;
    public const int MaxSentByLength = 100;

    private StudentInvitation()
    {
    }

    public int StudentId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    /// <summary>Id of the invitation in authcraft-api (needed to resend it).</summary>
    public long ExternalInvitationId { get; private set; }

    public DateTime SentAt { get; private set; }

    public DateTime? ExpiresAt { get; private set; }

    public string SentBy { get; private set; } = string.Empty;

    public InvitationStatus Status { get; private set; }

    public static StudentInvitation Send(
        int studentId, string email, long externalInvitationId, DateTime? expiresAtUtc, string sentBy, DateTime sentAtUtc) =>
        new()
        {
            StudentId = studentId,
            Email = email,
            ExternalInvitationId = externalInvitationId,
            ExpiresAt = expiresAtUtc,
            SentBy = sentBy,
            SentAt = sentAtUtc,
            Status = InvitationStatus.Sent
        };

    public void MarkAccepted()
    {
        if (Status == InvitationStatus.Sent)
        {
            Status = InvitationStatus.Accepted;
        }
    }

    public void MarkExpired()
    {
        if (Status == InvitationStatus.Sent)
        {
            Status = InvitationStatus.Expired;
        }
    }
}
