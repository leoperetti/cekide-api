using CekideApi.Application.Payments;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

/// <summary>
/// Owner-only payments screen and corrections. Monthly payments and passes live in different tables,
/// so <c>kind</c> (monthly | pass, default monthly) selects which id space <c>paymentId</c> belongs to.
/// </summary>
[ApiController]
[Route("api/gym/payments")]
[Authorize(Policy = Policies.OwnerOnly)]
public class PaymentsController(PaymentService payments) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PaymentRowDto>>> List(
        [FromQuery] string? status, [FromQuery] string? period, CancellationToken cancellationToken) =>
        Ok(await payments.ListRowsAsync(status, period, cancellationToken));

    [HttpPut("{paymentId:int}")]
    public async Task<ActionResult<PaymentDto>> Modify(
        int paymentId, ModifyPaymentRequest request, CancellationToken cancellationToken, [FromQuery] PaymentKind kind = PaymentKind.Monthly) =>
        Ok(await payments.ModifyAsync(kind, paymentId, request, User.GetActor(), cancellationToken));

    [HttpDelete("{paymentId:int}")]
    public async Task<IActionResult> Void(
        int paymentId, CancellationToken cancellationToken, [FromQuery] PaymentKind kind = PaymentKind.Monthly)
    {
        await payments.VoidAsync(kind, paymentId, User.GetActor(), cancellationToken);
        return NoContent();
    }

    [HttpGet("{paymentId:int}/history")]
    public async Task<ActionResult<IReadOnlyList<PaymentHistoryDto>>> History(
        int paymentId, CancellationToken cancellationToken, [FromQuery] PaymentKind kind = PaymentKind.Monthly) =>
        Ok(await payments.HistoryAsync(kind, paymentId, cancellationToken));
}
