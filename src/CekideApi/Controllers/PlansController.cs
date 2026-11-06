using CekideApi.Application.Plans;
using CekideApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym")]
public class PlansController(PlanService plans) : ControllerBase
{
    [HttpGet("plans")]
    [Authorize(Policy = Policies.StaffOnly)]
    public async Task<ActionResult<IReadOnlyList<PlanDto>>> List(CancellationToken cancellationToken) =>
        Ok(await plans.ListAsync(cancellationToken));

    [HttpPost("plans")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<ActionResult<PlanDto>> Create(CreatePlanRequest request, CancellationToken cancellationToken)
    {
        var plan = await plans.CreateAsync(request, User.GetSubject(), cancellationToken);
        return Created($"/api/gym/plans/{plan.Id}", plan);
    }

    [HttpPut("plans/{planId:int}")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<ActionResult<PlanDto>> Update(int planId, UpdatePlanRequest request, CancellationToken cancellationToken) =>
        Ok(await plans.UpdateAsync(planId, request, User.GetSubject(), cancellationToken));

    [HttpGet("pass-tariffs")]
    [Authorize(Policy = Policies.StaffOnly)]
    public async Task<ActionResult<IReadOnlyList<PassTariffDto>>> ListTariffs(CancellationToken cancellationToken) =>
        Ok(await plans.ListTariffsAsync(cancellationToken));

    [HttpPut("pass-tariffs")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public async Task<ActionResult<IReadOnlyList<PassTariffDto>>> SetTariffs(
        PassTariffRequest[] request, CancellationToken cancellationToken) =>
        Ok(await plans.SetTariffsAsync(request, cancellationToken));
}
