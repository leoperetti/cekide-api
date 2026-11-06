using System.Globalization;
using System.Security.Claims;
using CekideApi.Application.Attendance;
using CekideApi.Application.Kiosks;
using CekideApi.Application.Students;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym")]
public sealed class AttendanceCheckInController(
    AttendanceCheckInService attendance,
    StudentLinkingService linking) : ControllerBase
{
    [HttpPost("me/attendance/check-in")]
    [Authorize(Policy = Policies.StudentOnly)]
    public async Task<ActionResult<AttendanceCheckInResultDto>> CheckInSelf(
        AttendanceCheckInRequest request,
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
        var result = await attendance.CheckInSelfAsync(student.Id, request.Dni, cancellationToken);
        return Result(result);
    }

    [HttpPost("kiosk/attendance/check-in")]
    [Authorize(Policy = Policies.KioskOnly)]
    [EnableRateLimiting("kiosk-check-in")]
    public async Task<ActionResult<AttendanceCheckInResultDto>> CheckInKiosk(
        AttendanceCheckInRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var tenant = User.FindFirstValue(TenantRequirementHandler.TenantClaim);
        var sessionIdText = User.FindFirstValue(KioskAuthenticationHandler.SessionIdClaim);
        if (string.IsNullOrWhiteSpace(tenant) ||
            !int.TryParse(sessionIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionId))
        {
            throw new AttendanceException(401, "kiosk_not_authorized", "Kiosk session is not authorized.");
        }

        var result = await attendance.CheckInKioskByDniAsync(
            request.Dni, sessionId, tenant, cancellationToken);
        return Result(result);
    }

    private ActionResult<AttendanceCheckInResultDto> Result(AttendanceCheckInResultDto result) =>
        result.Outcome == AttendanceCheckInOutcome.Created
            ? StatusCode(StatusCodes.Status201Created, result)
            : Ok(result);
}
