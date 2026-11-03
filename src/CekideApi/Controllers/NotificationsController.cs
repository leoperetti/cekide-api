using CekideApi.Application.Notifications;
using CekideApi.Authorization;
using CekideApi.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CekideApi.Controllers;

[ApiController]
[Route("api/gym/notifications")]
[Authorize(Policy = Policies.StaffOnly)]
public class NotificationsController(OverdueNotificationService notifications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(
        [FromQuery] string? period, [FromQuery] NotificationStatus? status, CancellationToken cancellationToken) =>
        Ok(await notifications.ListAsync(period, status, cancellationToken));

    [HttpPost("{notificationId:int}/retry")]
    public async Task<ActionResult<NotificationDto>> Retry(int notificationId, CancellationToken cancellationToken) =>
        Accepted(await notifications.RetryAsync(notificationId, cancellationToken));
}
