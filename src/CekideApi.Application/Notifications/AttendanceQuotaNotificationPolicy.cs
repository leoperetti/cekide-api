namespace CekideApi.Application.Notifications;

public static class AttendanceQuotaNotificationPolicy
{
    public const int BatchSize = 100;
    public const int MaximumAutomaticRetries = 3;
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(3);
    public const string DeliveryUnknownError = "The provider response was not received; delivery is unknown.";
}
