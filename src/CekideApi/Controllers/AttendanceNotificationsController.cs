using CekideApi.Application.Notifications;
using CekideApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/attendance-notifications")]
[Authorize(Policy = Policies.StaffOnly)]
public sealed class AttendanceNotificationsController(AttendanceQuotaNotificationService notifications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AttendanceQuotaNotificationDto>>> List(
        [FromQuery] string? period,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await notifications.ListAsync(period, status, cancellationToken));
    }

    [HttpPost("{notificationId:int}/retry")]
    public async Task<ActionResult<AttendanceQuotaNotificationDto>> Retry(
        int notificationId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Accepted(await notifications.QueueManualRetryAsync(notificationId, cancellationToken));
    }

    [HttpPost("{notificationId:int}/resolve-delivery")]
    public async Task<ActionResult<AttendanceQuotaNotificationDto>> ResolveDelivery(
        int notificationId,
        ResolveAttendanceQuotaDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await notifications.ResolveDeliveryAsync(
            notificationId, request.Outcome, User.GetActor(), request.Evidence, cancellationToken));
    }
}
