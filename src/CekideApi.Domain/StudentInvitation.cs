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

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    public string? FullName { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? Dni { get; private set; }

    public bool RequiresRegistrationProfile { get; private set; }

    public DateTime? ProfileSyncedAt { get; private set; }

    public StudentInvitationProfile? RegistrationProfile => RequiresRegistrationProfile
        ? new StudentInvitationProfile(StudentId, FirstName, LastName, FullName ?? string.Empty, PhoneNumber, Dni)
        : null;

    public static StudentInvitation Send(
        int studentId,
        string email,
        long externalInvitationId,
        DateTime? expiresAtUtc,
        string sentBy,
        DateTime sentAtUtc,
        StudentInvitationProfile? registrationProfile = null,
        bool requiresRegistrationProfile = false)
    {
        if (requiresRegistrationProfile && registrationProfile is null)
        {
            throw new DomainValidationException("A registration profile is required for this invitation.");
        }

        if (registrationProfile is not null && registrationProfile.StudentId != studentId)
        {
            throw new DomainValidationException("The registration profile belongs to another student.");
        }

        return new StudentInvitation
        {
            StudentId = studentId,
            Email = email,
            ExternalInvitationId = externalInvitationId,
            ExpiresAt = expiresAtUtc,
            SentBy = sentBy,
            SentAt = sentAtUtc,
            Status = InvitationStatus.Sent,
            FirstName = registrationProfile?.FirstName,
            LastName = registrationProfile?.LastName,
            FullName = registrationProfile?.FullName,
            PhoneNumber = registrationProfile?.PhoneNumber,
            Dni = registrationProfile?.Dni,
            RequiresRegistrationProfile = requiresRegistrationProfile
        };
    }

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

    public void MarkProfileSynced(DateTime syncedAtUtc)
    {
        if (!RequiresRegistrationProfile)
        {
            throw new ConflictException("This invitation does not require profile synchronization.");
        }

        ProfileSyncedAt = DateTime.SpecifyKind(syncedAtUtc, DateTimeKind.Utc);
    }
}
