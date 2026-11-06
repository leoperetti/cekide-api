using CekideApi.Application.Shifts;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/shifts")]
[Authorize(Policy = Policies.StaffOnly)]
public sealed class ShiftsController(ShiftService shifts) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GymShiftDto>>> List(
        [FromQuery] string? period,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await shifts.ListAsync(period, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<GymShiftDto>> Create(
        CreateShiftRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var shift = await shifts.CreateAsync(request, User.GetSubject(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, shift);
    }

    [HttpPost("{shiftId:int}/cancel")]
    public async Task<ActionResult<GymShiftDto>> Cancel(
        int shiftId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await shifts.CancelAsync(shiftId, User.GetSubject(), cancellationToken));
    }

    [HttpGet("{shiftId:int}/occupancy")]
    public async Task<ActionResult<ShiftOccupancyDto>> Occupancy(int shiftId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await shifts.GetOccupancyAsync(shiftId, cancellationToken));
    }

    [HttpPost("{shiftId:int}/habitual-occupancies")]
    public async Task<ActionResult<HabitualOccupancyDto>> AddHabitual(
        int shiftId,
        AddHabitualOccupancyRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await shifts.AddHabitualAsync(shiftId, request.StudentId, User.GetSubject(), cancellationToken);
        var dto = (await shifts.GetOccupancyAsync(shiftId, cancellationToken))
            .HabitualOccupancies.Single(occupancy => occupancy.Id == result.Occupancy.Id);
        return result.Created
            ? StatusCode(StatusCodes.Status201Created, dto)
            : Ok(dto);
    }

    [HttpPost("{shiftId:int}/habitual-occupancies/{occupancyId:int}/remove")]
    public async Task<ActionResult<ShiftHabitualOccupancy>> RemoveHabitual(
        int shiftId,
        int occupancyId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var occupancy = await shifts.RemoveHabitualAsync(
            shiftId, occupancyId, User.GetSubject(), cancellationToken);
        return Ok(occupancy);
    }
}
