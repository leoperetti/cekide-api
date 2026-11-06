using CekideApi.Application.Recoveries;
using CekideApi.Application.Students;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/me")]
[Authorize(Policy = Policies.StudentOnly)]
public sealed class MyRecoveriesController(
    RecoveryAvailabilityService availability,
    RecoveryReservationService reservations,
    StudentLinkingService linking) : ControllerBase
{
    [HttpGet("recovery-credits")]
    public async Task<ActionResult<RecoveryCreditsDto>> Credits(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var studentId = await GetStudentIdAsync(cancellationToken);
        return Ok(await availability.GetCreditsAsync(studentId, cancellationToken));
    }

    [HttpGet("recovery-availability")]
    public async Task<ActionResult<RecoveryAvailabilityDto>> Availability(
        [FromQuery(Name = "credit_id")] int? creditId,
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (creditId is null)
        {
            throw new AttendanceException(400, "invalid_recovery_request", "A valid credit id is required.");
        }

        var studentId = await GetStudentIdAsync(cancellationToken);
        return Ok(await availability.GetAvailabilityAsync(studentId, creditId.Value, period, cancellationToken));
    }

    [HttpGet("recovery-reservations")]
    public async Task<ActionResult<IReadOnlyList<RecoveryReservationDto>>> Reservations(
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var studentId = await GetStudentIdAsync(cancellationToken);
        return Ok(await availability.GetReservationsAsync(studentId, period, cancellationToken));
    }

    [HttpPost("recovery-reservations")]
    public async Task<ActionResult<RecoveryReservationDto>> Reserve(
        CreateRecoveryReservationRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var studentId = await GetStudentIdAsync(cancellationToken);
        var result = await reservations.ReserveAsync(studentId, request, cancellationToken);
        return result.Created
            ? StatusCode(StatusCodes.Status201Created, result.Reservation)
            : Ok(result.Reservation);
    }

    [HttpPost("recovery-reservations/{reservationId:int}/cancel")]
    public async Task<ActionResult<RecoveryReservationDto>> Cancel(
        int reservationId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var studentId = await GetStudentIdAsync(cancellationToken);
        return Ok(await reservations.CancelAsync(studentId, reservationId, cancellationToken));
    }

    private async Task<int> GetStudentIdAsync(CancellationToken cancellationToken)
    {
        var result = await linking.TryLinkAsync(
            User.GetSubject(), User.GetEmail(), Request.GetBearerToken(), cancellationToken);
        return result.Outcome switch
        {
            LinkOutcome.Linked or LinkOutcome.AlreadyLinked when result.Student is { IsActive: true } student =>
                student.Id,
            LinkOutcome.Inactive => throw new AttendanceException(409, "student_inactive", "The student is inactive."),
            _ => throw new NotFoundException("This account is not linked to a student.")
        };
    }
}
