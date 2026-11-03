using CekideApi.Domain;

namespace CekideApi.Application.Abstractions;

public interface IStudentInvitationRepository
{
    /// <summary>Every invitation of the student, oldest first.</summary>
    Task<IReadOnlyList<StudentInvitation>> ListForStudentAsync(int studentId, CancellationToken cancellationToken = default);

    /// <summary>Status of the latest invitation of each student that has one.</summary>
    Task<IReadOnlyDictionary<int, InvitationStatus>> LatestStatusesAsync(IEnumerable<int> studentIds, CancellationToken cancellationToken = default);

    void Add(StudentInvitation invitation);
}

public sealed record CreatedInvitation(long InvitationId, DateTimeOffset ExpiresAt);

/// <summary>authcraft-api invitations, called with the access token of the staff member who performs the action.</summary>
public interface IAuthcraftInvitationClient
{
    /// <summary>Throws ConflictException (pending invitation), DomainValidationException (invalid email),
    /// ForbiddenException, NotFoundException or UpstreamServiceException.</summary>
    Task<CreatedInvitation> CreateAsync(string email, string accessToken, CancellationToken cancellationToken = default);

    Task<CreatedInvitation> ResendAsync(long invitationId, string accessToken, CancellationToken cancellationToken = default);
}
