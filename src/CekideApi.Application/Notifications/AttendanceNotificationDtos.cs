using CekideApi.Domain;

namespace CekideApi.Application.Notifications;

public sealed record RecommendedAttendancePlanDto(int Id, string Name, int MonthlyDays, decimal MonthlyPrice);

public sealed record AttendanceQuotaNotificationDto(
    int Id,
    int StudentId,
    string StudentName,
    string Period,
    string Status,
    int Attempts,
    DateTime CreatedAt,
    DateTime? SentAt,
    string? LastError,
    RecommendedAttendancePlanDto? RecommendedPlan)
{
    public static AttendanceQuotaNotificationDto From(
        AttendanceQuotaNotification notification,
        AttendanceQuotaNotificationStatus? statusOverride = null) =>
        new(
            notification.Id,
            notification.StudentId,
            notification.RecipientName,
            notification.Period,
            (statusOverride ?? notification.Status) switch
            {
                AttendanceQuotaNotificationStatus.Pending => "pending",
                AttendanceQuotaNotificationStatus.Sending => "sending",
                AttendanceQuotaNotificationStatus.Sent => "sent",
                AttendanceQuotaNotificationStatus.Failed => "failed",
                AttendanceQuotaNotificationStatus.DeliveryUnknown => "delivery_unknown",
                _ => throw new ArgumentOutOfRangeException(nameof(notification), "Unknown quota notification status.")
            },
            notification.Attempts,
            notification.CreatedAt,
            notification.SentAt,
            notification.LastError,
            notification.RecommendedPlanId is { } id &&
            notification.RecommendedPlanName is { } name &&
            notification.RecommendedMonthlyDays is { } monthlyDays &&
            notification.RecommendedPrice is { } price
                ? new RecommendedAttendancePlanDto(id, name, monthlyDays, price)
                : null);
}

public sealed record ResolveAttendanceQuotaDeliveryRequest(string Outcome, string Evidence);

public sealed record AttendanceQuotaDispatchResult(int Sent, int Failed, int DeliveryUnknown, int ExpiredClaims);
