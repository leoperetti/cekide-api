using CekideApi.Application.Payments;
using CekideApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

/// <summary>The signed-in student's own payments; owner/teacher accounts without the user role are rejected by the policy.</summary>
[ApiController]
[Route("api/gym/me/payments")]
[Authorize(Policy = Policies.StudentOnly)]
public class MyPaymentsController(MyPaymentsService myPayments) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MyPaymentDto>>> List(CancellationToken cancellationToken) =>
        Ok(await myPayments.ListAsync(User.GetSubject(), User.GetEmail(), Request.GetBearerToken(), cancellationToken));
}
