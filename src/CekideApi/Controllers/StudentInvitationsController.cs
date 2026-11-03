using CekideApi.Application.Students;
using CekideApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

/// <summary>Invitations are created and emailed by authcraft-api; this controller delegates with the caller's token.</summary>
[ApiController]
[Route("api/gym/students/{studentId:int}/invitations")]
[Authorize(Policy = Policies.StaffOnly)]
public class StudentInvitationsController(StudentInvitationService invitations) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<StudentInvitationDto>> Invite(int studentId, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status202Accepted,
            await invitations.InviteAsync(studentId, User.GetActor(), Request.GetBearerToken(), cancellationToken));

    [HttpPost("resend")]
    public async Task<ActionResult<StudentInvitationDto>> Resend(int studentId, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created,
            await invitations.ResendAsync(studentId, User.GetActor(), Request.GetBearerToken(), cancellationToken));
}
