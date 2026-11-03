using CekideApi.Application.Payments;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/students/{studentId:int}/payments")]
[Authorize(Policy = Policies.StaffOnly)]
public class StudentPaymentsController(PaymentService payments) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> List(int studentId, CancellationToken cancellationToken) =>
        Ok(await payments.ListForStudentAsync(studentId, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<PaymentDto>> Register(
        int studentId, RegisterPaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await payments.RegisterAsync(studentId, request, User.GetActor(), cancellationToken);
        var kind = payment.Kind == PaymentKind.Monthly ? "monthly" : "pass";
        return Created($"/api/gym/payments/{payment.Id}/history?kind={kind}", payment);
    }
}
