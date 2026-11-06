using CekideApi.Application.Kiosks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/kiosk/activate")]
[AllowAnonymous]
public sealed class KioskActivationController(KioskSessionService kiosks) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("kiosk-activation")]
    public async Task<ActionResult<KioskCredentialDto>> Activate(
        ActivateKioskRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await kiosks.ActivateAsync(request.ActivationCode, cancellationToken));
    }
}
