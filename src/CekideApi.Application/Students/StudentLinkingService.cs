using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Students;

public enum LinkOutcome
{
    Linked,
    AlreadyLinked,
    NotFound,
    Inactive,
    LinkedToAnotherIdentity
}

public sealed record LinkResult(LinkOutcome Outcome, Student? Student);

/// <summary>Links invited students through their accepted profile and retains email matching for legacy accounts.</summary>
public sealed class StudentLinkingService(
    IStudentRepository students,
    IStudentInvitationRepository invitations,
    IAuthcraftInvitationClient authcraft,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public Task<LinkResult> TryLinkAsync(
        string? identityId,
        string? email,
        CancellationToken cancellationToken = default) =>
        TryLinkAsync(identityId, email, null, cancellationToken);

    public async Task<LinkResult> TryLinkAsync(
        string? identityId,
        string? email,
        string? accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(identityId))
        {
            return new LinkResult(LinkOutcome.NotFound, null);
        }

        var identity = identityId.Trim();
        if (await students.GetByIdentityIdAsync(identity, cancellationToken) is { } linked)
        {
            return new LinkResult(LinkOutcome.AlreadyLinked, linked);
        }

        string normalizedEmail;
        try
        {
            normalizedEmail = Student.NormalizeEmail(email);
        }
        catch (DomainValidationException)
        {
            return new LinkResult(LinkOutcome.NotFound, null);
        }

        var invitation = await invitations.GetLatestByEmailAsync(normalizedEmail, cancellationToken);
        if (invitation?.RequiresRegistrationProfile == true)
        {
            return await LinkAcceptedProfileAsync(identity, invitation, accessToken, cancellationToken);
        }

        var student = await students.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (student is null)
        {
            return new LinkResult(LinkOutcome.NotFound, null);
        }

        if (student.IdentityId is not null)
        {
            return new LinkResult(LinkOutcome.LinkedToAnotherIdentity, null);
        }

        if (!student.IsActive)
        {
            return new LinkResult(LinkOutcome.Inactive, null);
        }

        student.LinkIdentity(identity);
        foreach (var previousInvitation in await invitations.ListForStudentAsync(student.Id, cancellationToken))
        {
            previousInvitation.MarkAccepted();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new LinkResult(LinkOutcome.Linked, student);
    }

    private async Task<LinkResult> LinkAcceptedProfileAsync(
        string identity,
        StudentInvitation invitation,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        if (invitation.ProfileSyncedAt is not null)
        {
            var linkedStudent = await students.GetByIdAsync(invitation.StudentId, cancellationToken);
            if (linkedStudent is null)
            {
                throw new NotFoundException("The invited student no longer exists.");
            }

            if (string.Equals(linkedStudent.IdentityId, identity, StringComparison.Ordinal))
            {
                return new LinkResult(LinkOutcome.AlreadyLinked, linkedStudent);
            }

            return linkedStudent.IdentityId is null
                ? throw new ConflictException("The accepted student profile has not been synchronized.")
                : new LinkResult(LinkOutcome.LinkedToAnotherIdentity, null);
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ForbiddenException("An authenticated access token is required to synchronize this invitation.");
        }

        AcceptedStudentProfile? accepted;
        try
        {
            accepted = await authcraft.GetAcceptedProfileAsync(invitation.ExternalInvitationId, accessToken, cancellationToken);
        }
        catch (NotFoundException)
        {
            throw new ConflictException("The student invitation has not been accepted.");
        }

        if (accepted is null
            || accepted.InvitationId != invitation.ExternalInvitationId
            || accepted.StudentId != invitation.StudentId
            || !string.Equals(accepted.IdentityId, identity, StringComparison.Ordinal))
        {
            throw new ConflictException("The accepted profile does not match this student invitation.");
        }

        var student = await students.GetByIdAsync(accepted.StudentId, cancellationToken);
        if (student is null)
        {
            throw new NotFoundException("The invited student no longer exists.");
        }

        if (!student.IsActive)
        {
            return new LinkResult(LinkOutcome.Inactive, student);
        }

        if (student.IdentityId is not null && student.IdentityId != identity)
        {
            return new LinkResult(LinkOutcome.LinkedToAnotherIdentity, null);
        }

        var registeredDni = Student.NormalizeDni(accepted.Registered.Dni);
        if (await students.DniExistsAsync(registeredDni, student.Id, cancellationToken))
        {
            throw new ConflictException("The registered DNI is already assigned to another student.");
        }

        student.SyncRegistrationProfile(accepted.Prefill, accepted.Registered);
        student.LinkIdentity(identity);
        invitation.MarkAccepted();
        invitation.MarkProfileSynced(clock.UtcNow.UtcDateTime);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new LinkResult(LinkOutcome.Linked, student);
    }
}
