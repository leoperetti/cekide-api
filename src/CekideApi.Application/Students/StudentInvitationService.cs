using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Students;

/// <param name="Status">sent | accepted | expired</param>
public sealed record StudentInvitationDto(
    int Id, int StudentId, string Email, InvitationStatus Status, DateTime SentAt, string SentBy, DateTime? ExpiresAt)
{
    public static StudentInvitationDto From(StudentInvitation i) =>
        new(i.Id, i.StudentId, i.Email, i.Status, i.SentAt, i.SentBy, i.ExpiresAt);
}

/// <summary>Invites students through authcraft-api (which owns codes and emails) and keeps a local record.</summary>
public sealed class StudentInvitationService(
    IStudentRepository students,
    IStudentInvitationRepository invitations,
    IAuthcraftInvitationClient authcraft,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<StudentInvitationDto> InviteAsync(
        int studentId, string actor, string accessToken, CancellationToken cancellationToken = default)
    {
        var student = await RequireInvitableAsync(studentId, cancellationToken);

        var created = await authcraft.CreateAsync(student.Email, RegistrationProfile(student), accessToken, cancellationToken);
        return await RecordAsync(student, created, actor, cancellationToken);
    }

    public async Task<StudentInvitationDto> ResendAsync(
        int studentId, string actor, string accessToken, CancellationToken cancellationToken = default)
    {
        var student = await RequireInvitableAsync(studentId, cancellationToken);

        var latest = (await invitations.ListForStudentAsync(studentId, cancellationToken)).LastOrDefault()
                     ?? throw new NotFoundException($"Student {studentId} has no previous invitation.");
        if (latest.Status == InvitationStatus.Accepted)
        {
            throw new ConflictException("The student already accepted the invitation.");
        }

        var created = await authcraft.ResendAsync(
            latest.ExternalInvitationId, student.Email, RegistrationProfile(student), accessToken, cancellationToken);
        return await RecordAsync(student, created, actor, cancellationToken);
    }

    private async Task<Student> RequireInvitableAsync(int studentId, CancellationToken cancellationToken)
    {
        var student = await students.GetByIdAsync(studentId, cancellationToken)
                      ?? throw new NotFoundException($"Student {studentId} was not found.");

        if (!student.IsActive)
        {
            throw new DomainValidationException("Inactive students cannot be invited.");
        }

        if (student.IdentityId is not null)
        {
            throw new ConflictException("The student already has a linked account.");
        }

        return student;
    }

    /// <summary>The new invitation supersedes every earlier one that was still marked as sent.</summary>
    private async Task<StudentInvitationDto> RecordAsync(
        Student student, CreatedInvitation created, string actor, CancellationToken cancellationToken)
    {
        foreach (var previous in await invitations.ListForStudentAsync(student.Id, cancellationToken))
        {
            previous.MarkExpired();
        }

        var invitation = StudentInvitation.Send(
            student.Id,
            student.Email,
            created.InvitationId,
            created.ExpiresAt.UtcDateTime,
            actor,
            clock.UtcNow.UtcDateTime,
            RegistrationProfile(student),
            requiresRegistrationProfile: true);
        invitations.Add(invitation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return StudentInvitationDto.From(invitation);
    }

    private static StudentInvitationProfile RegistrationProfile(Student student) =>
        new(student.Id, student.FirstName, student.LastName, student.FullName, student.Phone, student.Dni);
}
