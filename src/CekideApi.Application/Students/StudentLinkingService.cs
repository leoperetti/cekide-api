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

/// <summary>Links an authcraft account ("sub") to the student whose email matches the account's email.</summary>
public sealed class StudentLinkingService(
    IStudentRepository students,
    IStudentInvitationRepository invitations,
    IUnitOfWork unitOfWork)
{
    public async Task<LinkResult> TryLinkAsync(string? identityId, string? email, CancellationToken cancellationToken = default)
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
        foreach (var invitation in await invitations.ListForStudentAsync(student.Id, cancellationToken))
        {
            invitation.MarkAccepted();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new LinkResult(LinkOutcome.Linked, student);
    }
}
