using CekideApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.IntegrationTests.Infrastructure;

/// <summary>Test-only endpoints to exercise each authorization policy in isolation.</summary>
[ApiController]
[Route("test/policies")]
public class PolicyProbeController : ControllerBase
{
    [HttpGet("owner")]
    [Authorize(Policy = Policies.OwnerOnly)]
    public IActionResult Owner() => Ok();

    [HttpGet("staff")]
    [Authorize(Policy = Policies.StaffOnly)]
    public IActionResult Staff() => Ok();

    [HttpGet("student")]
    [Authorize(Policy = Policies.StudentOnly)]
    public IActionResult Student() => Ok();
}
