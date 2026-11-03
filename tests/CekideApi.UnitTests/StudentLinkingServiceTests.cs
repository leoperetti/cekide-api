using CekideApi.Application.Students;
using CekideApi.Domain;
using CekideApi.UnitTests.Support;

namespace CekideApi.UnitTests;

public class StudentLinkingServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryStudentRepository _students = new();
    private readonly InMemoryStudentInvitationRepository _invitations = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private StudentLinkingService CreateService() => new(_students, _invitations, _unitOfWork);

    private Student Seed(string email = "ana@example.com", bool active = true) =>
        _students.Seed("Ana Pérez", email, planId: null, new DateOnly(2026, 10, 1), active);

    private StudentInvitation SeedInvitation(Student student)
    {
        var invitation = StudentInvitation.Send(student.Id, student.Email, 77, Now.AddDays(7), "teacher-1", Now);
        _invitations.Add(invitation);
        return invitation;
    }

    [Fact]
    public async Task Links_the_identity_when_the_subject_and_the_email_match_a_student()
    {
        var student = Seed("ana@example.com");

        var result = await CreateService().TryLinkAsync("identity-1", "ana@example.com");

        Assert.Equal(LinkOutcome.Linked, result.Outcome);
        Assert.Same(student, result.Student);
        Assert.Equal("identity-1", student.IdentityId);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task The_email_match_ignores_case_and_surrounding_spaces()
    {
        var student = Seed("ana@example.com");

        var result = await CreateService().TryLinkAsync("identity-1", "  Ana@Example.COM ");

        Assert.Equal(LinkOutcome.Linked, result.Outcome);
        Assert.Equal("identity-1", student.IdentityId);
    }

    [Fact]
    public async Task Linking_marks_the_sent_invitations_as_accepted()
    {
        var student = Seed();
        var sent = SeedInvitation(student);
        var expired = SeedInvitation(student);
        expired.MarkExpired();

        await CreateService().TryLinkAsync("identity-1", "ana@example.com");

        Assert.Equal(InvitationStatus.Accepted, sent.Status);
        Assert.Equal(InvitationStatus.Expired, expired.Status);
    }

    [Fact]
    public async Task A_different_email_does_not_link()
    {
        var student = Seed("ana@example.com");

        var result = await CreateService().TryLinkAsync("identity-1", "otra@example.com");

        Assert.Equal(LinkOutcome.NotFound, result.Outcome);
        Assert.Null(result.Student);
        Assert.Null(student.IdentityId);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task An_inactive_student_is_not_linked()
    {
        var student = Seed(active: false);
        var invitation = SeedInvitation(student);

        var result = await CreateService().TryLinkAsync("identity-1", "ana@example.com");

        Assert.Equal(LinkOutcome.Inactive, result.Outcome);
        Assert.Null(student.IdentityId);
        Assert.Equal(InvitationStatus.Sent, invitation.Status);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task An_already_linked_identity_is_returned_without_changes()
    {
        var student = Seed();
        student.LinkIdentity("identity-1");

        var result = await CreateService().TryLinkAsync("identity-1", "whatever@example.com");

        Assert.Equal(LinkOutcome.AlreadyLinked, result.Outcome);
        Assert.Same(student, result.Student);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    [Fact]
    public async Task A_student_linked_to_another_identity_is_not_relinked()
    {
        var student = Seed();
        student.LinkIdentity("identity-1");

        var result = await CreateService().TryLinkAsync("identity-2", "ana@example.com");

        Assert.Equal(LinkOutcome.LinkedToAnotherIdentity, result.Outcome);
        Assert.Equal("identity-1", student.IdentityId);
    }

    [Theory]
    [InlineData(null, "ana@example.com")]
    [InlineData("", "ana@example.com")]
    [InlineData("identity-1", null)]
    [InlineData("identity-1", "")]
    [InlineData("identity-1", "not-an-email")]
    public async Task Missing_or_invalid_claims_never_link(string? identityId, string? email)
    {
        var student = Seed();

        var result = await CreateService().TryLinkAsync(identityId, email);

        Assert.Equal(LinkOutcome.NotFound, result.Outcome);
        Assert.Null(student.IdentityId);
    }
}

public class StudentLinkIdentityTests
{
    private static Student NewStudent() => Student.Create("Ana Pérez", "ana@example.com", null, null, new DateOnly(2026, 11, 3));

    [Fact]
    public void LinkIdentity_stores_the_identity_and_is_idempotent_for_the_same_one()
    {
        var student = NewStudent();

        student.LinkIdentity("identity-1");
        student.LinkIdentity("identity-1");

        Assert.Equal("identity-1", student.IdentityId);
    }

    [Fact]
    public void LinkIdentity_refuses_a_different_identity()
    {
        var student = NewStudent();
        student.LinkIdentity("identity-1");

        Assert.Throws<ConflictException>(() => student.LinkIdentity("identity-2"));
    }

    [Fact]
    public void LinkIdentity_refuses_an_inactive_student()
    {
        var student = NewStudent();
        student.Deactivate();

        Assert.Throws<ConflictException>(() => student.LinkIdentity("identity-1"));
        Assert.Null(student.IdentityId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LinkIdentity_requires_an_identity(string? identity)
    {
        Assert.Throws<DomainValidationException>(() => NewStudent().LinkIdentity(identity));
    }

    [Fact]
    public void LinkIdentity_rejects_identities_longer_than_the_column()
    {
        Assert.Throws<DomainValidationException>(() => NewStudent().LinkIdentity(new string('x', Student.MaxIdentityIdLength + 1)));
    }
}

public class StudentInvitationEntityTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);

    private static StudentInvitation NewInvitation() => StudentInvitation.Send(3, "ana@example.com", 41, Now.AddDays(7), "teacher-1", Now);

    [Fact]
    public void A_new_invitation_is_sent_with_its_authcraft_reference()
    {
        var invitation = NewInvitation();

        Assert.Equal(3, invitation.StudentId);
        Assert.Equal("ana@example.com", invitation.Email);
        Assert.Equal(41, invitation.ExternalInvitationId);
        Assert.Equal(Now.AddDays(7), invitation.ExpiresAt);
        Assert.Equal("teacher-1", invitation.SentBy);
        Assert.Equal(Now, invitation.SentAt);
        Assert.Equal(InvitationStatus.Sent, invitation.Status);
    }

    [Fact]
    public void Only_a_sent_invitation_can_be_accepted_or_expired()
    {
        var accepted = NewInvitation();
        accepted.MarkAccepted();
        accepted.MarkExpired();
        Assert.Equal(InvitationStatus.Accepted, accepted.Status);

        var expired = NewInvitation();
        expired.MarkExpired();
        expired.MarkAccepted();
        Assert.Equal(InvitationStatus.Expired, expired.Status);
    }
}
