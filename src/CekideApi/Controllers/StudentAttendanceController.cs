using CekideApi.Application.Attendance;
using CekideApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/students/{studentId:int}/attendance")]
[Authorize(Policy = Policies.StaffOnly)]
public sealed class StudentAttendanceController(AttendanceCalendarService attendance) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AttendanceCalendarDto>> GetStudentAttendance(
        int studentId,
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await attendance.GetForStaffAsync(studentId, period, cancellationToken));
    }
}
