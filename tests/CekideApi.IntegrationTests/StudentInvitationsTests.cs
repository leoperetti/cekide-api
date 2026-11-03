using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CekideApi.Domain;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public class StudentInvitationsTests : ApiTestBase
{
    private static string TokenOf(HttpClient client) => client.DefaultRequestHeaders.Authorization!.Parameter!;

    private static int Id(JsonElement element) => element.GetProperty("id").GetInt32();

    private async Task<int> StudentAsync(string email = "ana@example.com") => Id(await CreateStudentAsync(email: email));

    private Task<HttpResponseMessage> InviteAsync(int studentId, HttpClient? client = null) =>
        (client ?? Teacher).PostAsync($"/api/gym/students/{studentId}/invitations", null);

    private Task<HttpResponseMessage> ResendAsync(int studentId, HttpClient? client = null) =>
        (client ?? Teacher).PostAsync($"/api/gym/students/{studentId}/invitations/resend", null);

    private Task<List<StudentInvitation>> StoredAsync() =>
        Factory.WithDbAsync(db => db.StudentInvitations.AsNoTracking().OrderBy(i => i.Id).ToListAsync());

    [Fact]
    public async Task Staff_invites_a_student_delegating_to_authcraft_with_the_callers_token()
    {
        var studentId = await StudentAsync("ana@example.com");

        var response = await InviteAsync(studentId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.True(Id(body) > 0);
        Assert.Equal(studentId, body.GetProperty("student_id").GetInt32());
        Assert.Equal("ana@example.com", body.GetProperty("email").GetString());
        Assert.Equal("sent", body.GetProperty("status").GetString());
        Assert.Equal("teacher-1", body.GetProperty("sent_by").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("sent_at").ValueKind);
        Assert.Equal(JsonValueKind.String, body.GetProperty("expires_at").ValueKind);

        var call = Assert.Single(Factory.Authcraft.Calls);
        Assert.Equal("create", call.Operation);
        Assert.Equal("ana@example.com", call.Email);
        Assert.Equal(TokenOf(Teacher), call.AccessToken);

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal(studentId, stored.StudentId);
        Assert.Equal("ana@example.com", stored.Email);
        Assert.Equal(InvitationStatus.Sent, stored.Status);
        Assert.Equal(100, stored.ExternalInvitationId);
        Assert.Equal("teacher-1", stored.SentBy);
        Assert.Equal(Factory.Clock.UtcNow.UtcDateTime, stored.SentAt);
    }

    [Fact]
    public async Task The_owner_can_invite_too_and_the_owners_token_is_forwarded()
    {
        var studentId = await StudentAsync();

        Assert.Equal(HttpStatusCode.Accepted, (await InviteAsync(studentId, Owner)).StatusCode);

        Assert.Equal(TokenOf(Owner), Assert.Single(Factory.Authcraft.Calls).AccessToken);
    }

    [Theory]
    [InlineData(409, HttpStatusCode.Conflict)]
    [InlineData(400, HttpStatusCode.BadRequest)]
    [InlineData(403, HttpStatusCode.Forbidden)]
    [InlineData(502, HttpStatusCode.BadGateway)]
    public async Task An_authcraft_rejection_is_propagated_and_nothing_is_recorded(int upstream, HttpStatusCode expected)
    {
        var studentId = await StudentAsync();
        Factory.Authcraft.FailNext = upstream switch
        {
            409 => new ConflictException("There is already a pending invitation for this email."),
            400 => new DomainValidationException("The invitation email is not valid."),
            403 => new ForbiddenException("Not allowed to invite."),
            _ => new UpstreamServiceException("authcraft-api is unavailable.")
        };

        var response = await InviteAsync(studentId);

        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(await StoredAsync());
    }

    [Fact]
    public async Task Inviting_a_missing_student_is_not_found_and_does_not_call_authcraft()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await InviteAsync(999)).StatusCode);
        Assert.Empty(Factory.Authcraft.Calls);
    }

    [Fact]
    public async Task Inactive_students_cannot_be_invited()
    {
        var studentId = await StudentAsync();
        await Teacher.PostAsync($"/api/gym/students/{studentId}/deactivate", null);

        Assert.Equal(HttpStatusCode.BadRequest, (await InviteAsync(studentId)).StatusCode);
        Assert.Empty(Factory.Authcraft.Calls);
    }

    [Fact]
    public async Task A_student_that_is_already_linked_cannot_be_invited_again()
    {
        var studentId = await StudentAsync();
        await Factory.WithDbAsync(async db =>
        {
            (await db.Students.SingleAsync()).LinkIdentity("identity-1");
            return await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Conflict, (await InviteAsync(studentId)).StatusCode);
        Assert.Empty(Factory.Authcraft.Calls);
    }

    [Fact]
    public async Task A_new_invitation_expires_the_previous_sent_one()
    {
        var studentId = await StudentAsync();
        await InviteAsync(studentId);

        await InviteAsync(studentId);

        var stored = await StoredAsync();
        Assert.Equal([InvitationStatus.Expired, InvitationStatus.Sent], stored.Select(i => i.Status));
    }

    [Fact]
    public async Task Resend_delegates_with_the_previous_invitation_and_leaves_the_old_one_expired()
    {
        var studentId = await StudentAsync();
        await InviteAsync(studentId);

        var response = await ResendAsync(studentId, Owner);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("sent", body.GetProperty("status").GetString());
        Assert.Equal("owner-1", body.GetProperty("sent_by").GetString());

        var resend = Factory.Authcraft.Calls.Last();
        Assert.Equal("resend", resend.Operation);
        Assert.Equal(100, resend.InvitationId);
        Assert.Equal(TokenOf(Owner), resend.AccessToken);

        var stored = await StoredAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal(InvitationStatus.Expired, stored[0].Status);
        Assert.Equal(InvitationStatus.Sent, stored[1].Status);
        Assert.Equal(101, stored[1].ExternalInvitationId);
        Assert.Equal(Id(body), stored[1].Id);
    }

    [Fact]
    public async Task Resending_without_a_previous_invitation_is_not_found()
    {
        var studentId = await StudentAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await ResendAsync(studentId)).StatusCode);
        Assert.Empty(Factory.Authcraft.Calls);
    }

    [Fact]
    public async Task Resending_for_a_missing_student_is_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await ResendAsync(999)).StatusCode);
    }

    [Fact]
    public async Task Resending_after_the_student_accepted_is_a_conflict()
    {
        var studentId = await StudentAsync();
        await InviteAsync(studentId);
        await Factory.WithDbAsync(async db =>
        {
            (await db.StudentInvitations.SingleAsync()).MarkAccepted();
            return await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.Conflict, (await ResendAsync(studentId)).StatusCode);
        Assert.Single(Factory.Authcraft.Calls);
    }

    [Fact]
    public async Task A_failed_resend_keeps_the_previous_invitation_untouched()
    {
        var studentId = await StudentAsync();
        await InviteAsync(studentId);
        Factory.Authcraft.FailNext = new NotFoundException("The invitation was not found.");

        Assert.Equal(HttpStatusCode.NotFound, (await ResendAsync(studentId)).StatusCode);

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal(InvitationStatus.Sent, stored.Status);
    }

    [Fact]
    public async Task Role_user_is_forbidden_and_anonymous_is_unauthorized()
    {
        var studentId = await StudentAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await InviteAsync(studentId, Student)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ResendAsync(studentId, Student)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await InviteAsync(studentId, Anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ResendAsync(studentId, Anonymous)).StatusCode);
        Assert.Empty(Factory.Authcraft.Calls);
    }

    // ---------- Student responses expose the link and invitation state ----------

    [Fact]
    public async Task Student_responses_start_without_invitation_status_or_identity()
    {
        var studentId = await StudentAsync();

        var detail = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}"));

        Assert.Equal(JsonValueKind.Null, detail.GetProperty("invitation_status").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("identity_id").ValueKind);
    }

    [Fact]
    public async Task Student_detail_and_list_show_the_latest_invitation_status()
    {
        var studentId = await StudentAsync();
        var otherId = await StudentAsync("beto@example.com");
        await InviteAsync(studentId);
        await InviteAsync(otherId);
        await ResendAsync(otherId);

        var detail = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}"));
        Assert.Equal("sent", detail.GetProperty("invitation_status").GetString());

        await Factory.WithDbAsync(async db =>
        {
            (await db.StudentInvitations.FirstAsync(i => i.StudentId == studentId)).MarkExpired();
            return await db.SaveChangesAsync();
        });
        detail = await ReadAsync(await Teacher.GetAsync($"/api/gym/students/{studentId}"));
        Assert.Equal("expired", detail.GetProperty("invitation_status").GetString());

        var list = await ReadAsync(await Teacher.GetAsync("/api/gym/students"));
        var byId = list.EnumerateArray().ToDictionary(Id);
        Assert.Equal("expired", byId[studentId].GetProperty("invitation_status").GetString());
        Assert.Equal("sent", byId[otherId].GetProperty("invitation_status").GetString());
    }

    [Fact]
    public async Task Student_detail_shows_the_linked_identity_and_accepted_status_after_the_student_signs_in()
    {
        var studentId = await StudentAsync("student-1@example.com");
        await InviteAsync(studentId);

        Assert.Equal(HttpStatusCode.OK, (await Student.GetAsync("/api/gym/me/payments")).StatusCode);

        var detail = await ReadAsync(await Owner.GetAsync($"/api/gym/students/{studentId}"));
        Assert.Equal("student-1", detail.GetProperty("identity_id").GetString());
        Assert.Equal("accepted", detail.GetProperty("invitation_status").GetString());
    }
}
