using System.Security.Claims;
using CekideApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

/// <summary>Echoes the authenticated identity; useful for the SPA and for smoke checks.</summary>
[ApiController]
[Route("api/gym/me")]
[Authorize]
public class MeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        sub = User.GetSubject(),
        tenant = User.FindFirstValue(TenantRequirementHandler.TenantClaim),
        roles = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()
    });
}
