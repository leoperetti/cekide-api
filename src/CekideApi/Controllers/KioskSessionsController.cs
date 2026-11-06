using System.Security.Claims;
using CekideApi.Application.Kiosks;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/kiosk-sessions")]
[Authorize(Policy = Policies.StaffOnly)]
public sealed class KioskSessionsController(KioskSessionService kiosks) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<KioskActivationDto>> Create(
        CreateKioskSessionRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var tenant = User.FindFirstValue(TenantRequirementHandler.TenantClaim);
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ForbiddenException("The staff tenant is required.");
        }

        var result = await kiosks.CreateAsync(request.Label, tenant, User.GetActor(), cancellationToken);
        return Created("/api/gym/kiosk-sessions", result);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<KioskSessionDto>>> List(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var tenant = User.FindFirstValue(TenantRequirementHandler.TenantClaim);
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ForbiddenException("The staff tenant is required.");
        }

        return Ok(await kiosks.ListAsync(tenant, cancellationToken));
    }

    [HttpPost("{id:int}/revoke")]
    public async Task<IActionResult> Revoke(int id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var tenant = User.FindFirstValue(TenantRequirementHandler.TenantClaim);
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ForbiddenException("The staff tenant is required.");
        }

        await kiosks.RevokeAsync(id, tenant, User.GetActor(), cancellationToken);
        return NoContent();
    }
}
