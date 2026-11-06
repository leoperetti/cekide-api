using CekideApi.Application.Recoveries;
using CekideApi.Application.Students;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/students/{studentId:int}/recovery-reservations")]
[Authorize(Policy = Policies.StaffOnly)]
public sealed class StudentRecoveriesController(RecoveryAvailabilityService availability) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecoveryReservationDto>>> List(
        int studentId,
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await availability.GetStudentReservationsAsync(studentId, period, cancellationToken));
    }
}
