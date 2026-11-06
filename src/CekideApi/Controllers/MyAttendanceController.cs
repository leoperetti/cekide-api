using CekideApi.Application.Attendance;
using CekideApi.Application.Students;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym")]
public sealed class MyAttendanceController(
    AttendanceCalendarService attendance,
    StudentLinkingService linking) : ControllerBase
{
    [HttpGet("me/attendance")]
    [Authorize(Policy = Policies.StudentOnly)]
    public async Task<ActionResult<AttendanceCalendarDto>> GetMyAttendance(
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var link = await linking.TryLinkAsync(
            User.GetSubject(), User.GetEmail(), Request.GetBearerToken(), cancellationToken);
        var student = link.Outcome switch
        {
            LinkOutcome.Linked or LinkOutcome.AlreadyLinked => link.Student!,
            LinkOutcome.Inactive => throw new AttendanceException(409, "student_inactive", "The student is inactive."),
            _ => throw new NotFoundException("This account is not linked to a student.")
        };
        return Ok(await attendance.GetAsync(student.Id, period, cancellationToken));
    }
}
