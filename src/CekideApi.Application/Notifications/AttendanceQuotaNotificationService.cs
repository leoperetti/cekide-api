using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Notifications;

public sealed class AttendanceQuotaNotificationService(
    IPlanRepository plans,
    IAttendanceRepository attendances,
    IAttendanceQuotaNotificationRepository notifications,
    IEmailSender email,
    IClock clock)
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5)
    ];

    public static bool ShouldCreateForCheckIn(
        AttendanceChargeKind chargeKind,
        RecoveryMonthUsage monthUsage,
        int consumedAfter,
        int monthlyLimit) =>
        chargeKind == AttendanceChargeKind.Ordinary &&
        monthUsage.OrdinaryAttended + monthUsage.RecoveryUsedFromOrigin < monthlyLimit &&
        consumedAfter >= monthlyLimit;

    public async Task<AttendanceQuotaNotification> CreateForExhaustedQuotaAsync(
        Student student,
        AttendancePeriod period,
        DateOnly localDate,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        var activePlans = (await plans.ListAsync(cancellationToken))
            .Where(plan => plan.IsActive)
            .ToArray();
        var limits = await plans.GetAttendanceLimitsAsync(activePlans.Select(plan => plan.Id), cancellationToken);
        var recommendation = activePlans
            .Select(plan => new
            {
                Plan = plan,
                Limits = limits.Where(limit => limit.PlanId == plan.Id && limit.EffectiveFrom <= localDate)
                    .MaxBy(limit => limit.EffectiveFrom)
            })
            .Where(candidate => candidate.Limits is not null &&
                                candidate.Limits.MonthlyDays > period.MonthlyLimit)
            .OrderBy(candidate => candidate.Limits!.MonthlyDays)
            .ThenBy(candidate => candidate.Plan.MonthlyPrice)
            .ThenBy(candidate => candidate.Plan.Id)
            .FirstOrDefault();

        var notification = AttendanceQuotaNotification.Create(
            student.Id,
            period.Id,
            period.Period,
            student.Email,
            student.FullName,
            period.PlanName,
            period.MonthlyLimit,
            createdAt,
            recommendation?.Plan.Id,
            recommendation?.Plan.Name,
            recommendation?.Limits?.MonthlyDays,
            recommendation?.Plan.MonthlyPrice);
        attendances.AddQuotaNotification(notification);
        return notification;
    }

    public async Task<IReadOnlyList<AttendanceQuotaNotificationDto>> ListAsync(
        string? period,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var normalizedPeriod = period is null ? null : PaymentPeriod.Parse(period).ToString();
        var normalizedStatus = ParseStatus(status);
        var found = await notifications.ListAsync(normalizedPeriod, normalizedStatus, cancellationToken);
        return found
            .OrderByDescending(notification => notification.Period, StringComparer.Ordinal)
            .ThenByDescending(notification => notification.Id)
            .Select(notification => AttendanceQuotaNotificationDto.From(notification))
            .ToArray();
    }

    public async Task<AttendanceQuotaNotificationDto> QueueManualRetryAsync(
        int notificationId,
        CancellationToken cancellationToken = default)
    {
        var notification = await notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new NotFoundException($"Quota notification {notificationId} was not found.");
        if (!await notifications.QueueManualRetryAsync(notificationId, cancellationToken))
        {
            throw new ConflictException("Only a failed quota notification can be retried.");
        }

        return AttendanceQuotaNotificationDto.From(notification, AttendanceQuotaNotificationStatus.Pending);
    }

    public async Task<AttendanceQuotaNotificationDto> ResolveDeliveryAsync(
        int notificationId,
        string? outcome,
        string? resolvedBy,
        string? evidence,
        CancellationToken cancellationToken = default)
    {
        var delivered = outcome?.Trim().ToLowerInvariant() switch
        {
            "sent" => true,
            "not_sent" => false,
            _ => throw new DomainValidationException("Delivery outcome must be sent or not_sent.")
        };
        var actor = resolvedBy?.Trim();
        var proof = evidence?.Trim();
        if (string.IsNullOrEmpty(actor) || actor.Length > KioskSession.MaxRevokedByLength ||
            string.IsNullOrEmpty(proof) || proof.Length > AttendanceQuotaNotification.MaxResolutionEvidenceLength)
        {
            throw new DomainValidationException("Delivery resolution requires an actor and evidence of at most 500 characters.");
        }

        if (await notifications.GetByIdAsync(notificationId, cancellationToken) is null)
        {
            throw new NotFoundException($"Quota notification {notificationId} was not found.");
        }

        if (!await notifications.ResolveDeliveryAsync(
                notificationId, delivered, actor, proof, clock.UtcNow, cancellationToken))
        {
            throw new ConflictException("Only a delivery-unknown quota notification can be resolved.");
        }

        return await GetDtoAsync(notificationId, cancellationToken);
    }

    public async Task<AttendanceQuotaDispatchResult> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var expiredClaims = await notifications.MarkExpiredClaimsAsync(now, cancellationToken);
        var ids = await notifications.ListRunnableIdsAsync(
            now, AttendanceQuotaNotificationPolicy.BatchSize, cancellationToken);
        var sent = 0;
        var failed = 0;
        var deliveryUnknown = 0;

        foreach (var id in ids)
        {
            var claimId = Guid.NewGuid().ToString("N");
            var claimAt = clock.UtcNow;
            if (!await notifications.TryClaimAsync(
                    id, claimId, claimAt, AttendanceQuotaNotificationPolicy.ClaimLease, cancellationToken))
            {
                continue;
            }

            var notification = await notifications.GetByIdAsync(id, cancellationToken)
                               ?? throw new InvalidOperationException($"Claimed quota notification {id} no longer exists.");
            EmailSendResult result;
            try
            {
                result = await email.SendAsync(BuildMessage(notification), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                await CompleteOrThrowAsync(
                    notifications.MarkDeliveryUnknownAsync(
                        id, claimId, AttendanceQuotaNotificationPolicy.DeliveryUnknownError, cancellationToken),
                    id);
                deliveryUnknown++;
                throw;
            }

            if (result.Succeeded)
            {
                await CompleteOrThrowAsync(
                    notifications.MarkSentAsync(id, claimId, clock.UtcNow, cancellationToken), id);
                sent++;
            }
            else if (result.DeliveryUncertain)
            {
                await CompleteOrThrowAsync(
                    notifications.MarkDeliveryUnknownAsync(
                        id,
                        claimId,
                        AttendanceQuotaNotificationPolicy.DeliveryUnknownError,
                        cancellationToken),
                    id);
                deliveryUnknown++;
            }
            else
            {
                var retryAt = notification.Attempts < RetryDelays.Length
                    ? clock.UtcNow.Add(RetryDelays[notification.Attempts])
                    : (DateTimeOffset?)null;
                await CompleteOrThrowAsync(
                    notifications.MarkFailedAsync(
                        id,
                        claimId,
                        "The email provider rejected the message.",
                        retryAt,
                        cancellationToken),
                    id);
                failed++;
            }
        }

        return new AttendanceQuotaDispatchResult(sent, failed, deliveryUnknown, expiredClaims);
    }

    private async Task<AttendanceQuotaNotificationDto> GetDtoAsync(
        int notificationId,
        CancellationToken cancellationToken)
    {
        var notification = await notifications.GetByIdAsync(notificationId, cancellationToken)
                           ?? throw new NotFoundException($"Quota notification {notificationId} was not found.");
        return AttendanceQuotaNotificationDto.From(notification);
    }

    private static AttendanceQuotaNotificationStatus? ParseStatus(string? status)
    {
        if (status is null)
        {
            return null;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "pending" => AttendanceQuotaNotificationStatus.Pending,
            "sending" => AttendanceQuotaNotificationStatus.Sending,
            "sent" => AttendanceQuotaNotificationStatus.Sent,
            "failed" => AttendanceQuotaNotificationStatus.Failed,
            "delivery_unknown" => AttendanceQuotaNotificationStatus.DeliveryUnknown,
            _ => throw new DomainValidationException("Status must be pending, sending, sent, failed, or delivery_unknown.")
        };
    }

    private static EmailMessage BuildMessage(AttendanceQuotaNotification notification)
    {
        var recommendation = notification.RecommendedPlanName is { } planName
            ? $"<p>El plan que sigue es <strong>{Encode(planName)}</strong>, con {notification.RecommendedMonthlyDays} días mensuales."
              + (notification.RecommendedPrice is { } price
                  ? $" Su valor mensual es ${price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}.</p>"
                  : "</p>")
            : "<p>Para continuar asistiendo, comunicate con el gimnasio y te asesoraremos sobre el plan adecuado.</p>";
        var body =
            $"<p>Hola {Encode(notification.RecipientName)},</p>" +
            $"<p>Alcanzaste el cupo de {notification.MonthlyLimit} días de tu plan {Encode(notification.PlanName)} para {Encode(notification.Period)}.</p>" +
            recommendation +
            "<p>¡Gracias!</p>";
        return new EmailMessage(
            notification.RecipientEmail,
            notification.RecipientName,
            $"Alcanzaste el cupo mensual de {notification.Period}",
            body);
    }

    private static string Encode(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static async Task CompleteOrThrowAsync(Task<bool> completion, int id)
    {
        if (!await completion)
        {
            throw new ConflictException($"The sending claim for quota notification {id} is no longer valid.");
        }
    }
}
