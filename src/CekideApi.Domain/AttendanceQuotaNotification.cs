namespace CekideApi.Domain;

public enum AttendanceQuotaNotificationStatus
{
    Pending,
    Sending,
    Sent,
    Failed,
    DeliveryUnknown
}

public sealed class AttendanceQuotaNotification : Entity
{
    public const int MaxRecipientNameLength = Student.MaxNameLength;
    public const int MaxErrorLength = 500;
    public const int MaxClaimIdLength = 100;
    public const int MaxResolutionEvidenceLength = 500;
    public const string ExpiredClaimError = "Delivery could not be confirmed before the sending claim expired.";
    public const string ConfirmedNotSentError = "Staff confirmed that the email was not delivered.";

    private AttendanceQuotaNotification()
    {
    }

    public int StudentId { get; private set; }

    public int AttendancePeriodId { get; private set; }

    public string Period { get; private set; } = string.Empty;

    public string RecipientEmail { get; private set; } = string.Empty;

    public string RecipientName { get; private set; } = string.Empty;

    public string PlanName { get; private set; } = string.Empty;

    public int MonthlyLimit { get; private set; }

    public int? RecommendedPlanId { get; private set; }

    public string? RecommendedPlanName { get; private set; }

    public int? RecommendedMonthlyDays { get; private set; }

    public decimal? RecommendedPrice { get; private set; }

    public AttendanceQuotaNotificationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? NextAttemptAt { get; private set; }

    public DateTime? ClaimedUntil { get; private set; }

    public string? ClaimId { get; private set; }

    public DateTime? SentAt { get; private set; }

    public string? LastError { get; private set; }

    public string? ResolvedBy { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public string? ResolutionEvidence { get; private set; }

    public static AttendanceQuotaNotification Create(
        int studentId,
        int attendancePeriodId,
        string period,
        string? recipientEmail,
        string? recipientName,
        string? planName,
        int monthlyLimit,
        DateTimeOffset utcNow,
        int? recommendedPlanId = null,
        string? recommendedPlanName = null,
        int? recommendedMonthlyDays = null,
        decimal? recommendedPrice = null)
    {
        var normalizedEmail = Student.NormalizeEmail(recipientEmail);
        var normalizedRecipient = recipientName?.Trim();
        var normalizedPlan = planName?.Trim();
        var normalizedRecommendedPlanName = recommendedPlanName?.Trim();
        var parsedPeriod = PaymentPeriod.Parse(period);

        if (studentId <= 0 || attendancePeriodId <= 0)
        {
            throw new DomainValidationException("Student and attendance period ids must be positive.");
        }

        if (string.IsNullOrEmpty(normalizedRecipient) || normalizedRecipient.Length > MaxRecipientNameLength)
        {
            throw new DomainValidationException("Recipient name is required and cannot exceed the student name length.");
        }

        if (string.IsNullOrEmpty(normalizedPlan) || normalizedPlan.Length > AttendancePeriod.MaxPlanNameLength ||
            monthlyLimit <= 0)
        {
            throw new DomainValidationException("Plan snapshot is invalid.");
        }

        ValidateRecommendation(recommendedPlanId, normalizedRecommendedPlanName, recommendedMonthlyDays, recommendedPrice, monthlyLimit);

        var now = utcNow.ToUniversalTime().UtcDateTime;
        if (utcNow == default)
        {
            throw new DomainValidationException("Notification creation time is required.");
        }

        return new AttendanceQuotaNotification
        {
            StudentId = studentId,
            AttendancePeriodId = attendancePeriodId,
            Period = parsedPeriod.ToString(),
            RecipientEmail = normalizedEmail,
            RecipientName = normalizedRecipient,
            PlanName = normalizedPlan,
            MonthlyLimit = monthlyLimit,
            RecommendedPlanId = recommendedPlanId,
            RecommendedPlanName = normalizedRecommendedPlanName,
            RecommendedMonthlyDays = recommendedMonthlyDays,
            RecommendedPrice = recommendedPrice,
            Status = AttendanceQuotaNotificationStatus.Pending
        };
    }

    public bool TryClaim(string? claimId, DateTimeOffset utcNow, TimeSpan claimDuration)
    {
        var now = utcNow.ToUniversalTime().UtcDateTime;
        var normalizedClaimId = claimId?.Trim();
        if (string.IsNullOrEmpty(normalizedClaimId) || normalizedClaimId.Length > MaxClaimIdLength)
        {
            throw new DomainValidationException($"Claim id is required and cannot exceed {MaxClaimIdLength} characters.");
        }

        if (claimDuration <= TimeSpan.Zero)
        {
            throw new DomainValidationException("Claim duration must be positive.");
        }

        if (Status is AttendanceQuotaNotificationStatus.Sent or AttendanceQuotaNotificationStatus.DeliveryUnknown or
            AttendanceQuotaNotificationStatus.Sending ||
            Status == AttendanceQuotaNotificationStatus.Failed &&
            (NextAttemptAt is null || NextAttemptAt > now))
        {
            return false;
        }

        Status = AttendanceQuotaNotificationStatus.Sending;
        ClaimId = normalizedClaimId;
        ClaimedUntil = now.Add(claimDuration);
        NextAttemptAt = null;
        return true;
    }

    public void ExpireClaim(DateTimeOffset expiredAtUtc)
    {
        var expiredAt = expiredAtUtc.ToUniversalTime().UtcDateTime;
        if (expiredAtUtc == default || Status != AttendanceQuotaNotificationStatus.Sending ||
            ClaimedUntil is null || ClaimedUntil > expiredAt)
        {
            throw new ConflictException("Only an expired sending claim can be marked delivery-unknown.");
        }

        Attempts++;
        Status = AttendanceQuotaNotificationStatus.DeliveryUnknown;
        LastError = ExpiredClaimError;
        ClearClaim();
    }

    public void QueueManualRetry()
    {
        if (Status != AttendanceQuotaNotificationStatus.Failed)
        {
            throw new ConflictException("Only a failed notification can be retried.");
        }

        Status = AttendanceQuotaNotificationStatus.Pending;
        NextAttemptAt = null;
    }

    public void MarkSent(string? claimId, DateTimeOffset sentAtUtc)
    {
        EnsureClaim(claimId);
        if (sentAtUtc == default)
        {
            throw new DomainValidationException("Notification sent time is required.");
        }

        Attempts++;
        Status = AttendanceQuotaNotificationStatus.Sent;
        SentAt = sentAtUtc.ToUniversalTime().UtcDateTime;
        LastError = null;
        ClearClaim();
    }

    public void MarkFailed(string? claimId, string? error, DateTimeOffset? nextAttemptAtUtc)
    {
        EnsureClaim(claimId);
        if (nextAttemptAtUtc == DateTimeOffset.MinValue)
        {
            throw new DomainValidationException("Next notification attempt time cannot be the minimum timestamp.");
        }

        Attempts++;
        Status = AttendanceQuotaNotificationStatus.Failed;
        NextAttemptAt = nextAttemptAtUtc?.ToUniversalTime().UtcDateTime;
        LastError = SanitizeError(error);
        ClearClaim();
    }

    public void MarkDeliveryUnknown(string? claimId, string? error)
    {
        EnsureClaim(claimId);
        Attempts++;
        Status = AttendanceQuotaNotificationStatus.DeliveryUnknown;
        LastError = SanitizeError(error);
        ClearClaim();
    }

    public void ResolveDelivery(string? resolvedBy, bool delivered, string? evidence, DateTimeOffset resolvedAtUtc)
    {
        if (Status != AttendanceQuotaNotificationStatus.DeliveryUnknown)
        {
            throw new ConflictException("Only a delivery-unknown notification can be resolved.");
        }

        var actor = resolvedBy?.Trim();
        var proof = evidence?.Trim();
        if (string.IsNullOrEmpty(actor) || actor.Length > KioskSession.MaxRevokedByLength ||
            string.IsNullOrEmpty(proof) || proof.Length > MaxResolutionEvidenceLength ||
            resolvedAtUtc == default)
        {
            throw new DomainValidationException("Delivery resolution requires an actor, evidence, and timestamp.");
        }

        ResolvedBy = actor;
        ResolutionEvidence = proof;
        ResolvedAt = resolvedAtUtc.ToUniversalTime().UtcDateTime;
        Status = delivered ? AttendanceQuotaNotificationStatus.Sent : AttendanceQuotaNotificationStatus.Failed;
        if (delivered)
        {
            SentAt = ResolvedAt;
            LastError = null;
        }
        else
        {
            NextAttemptAt = null;
            LastError = ConfirmedNotSentError;
        }
    }

    private static void ValidateRecommendation(
        int? planId,
        string? planName,
        int? monthlyDays,
        decimal? price,
        int currentMonthlyLimit)
    {
        if (planId is null && planName is null && monthlyDays is null && price is null)
        {
            return;
        }

        var normalizedName = planName?.Trim();
        if (planId is not > 0 || string.IsNullOrEmpty(normalizedName) ||
            normalizedName.Length > AttendancePeriod.MaxPlanNameLength ||
            monthlyDays is null || monthlyDays <= currentMonthlyLimit ||
            price is null || price < 0 || decimal.Round(price.Value, 2) != price.Value)
        {
            throw new DomainValidationException("Recommended plan snapshot must be complete and have a higher monthly limit.");
        }
    }

    private void EnsureClaim(string? claimId)
    {
        if (Status != AttendanceQuotaNotificationStatus.Sending ||
            string.IsNullOrWhiteSpace(claimId) ||
            !string.Equals(ClaimId, claimId.Trim(), StringComparison.Ordinal))
        {
            throw new ConflictException("Notification claim is no longer valid.");
        }
    }

    private void ClearClaim()
    {
        ClaimId = null;
        ClaimedUntil = null;
    }

    private static string SanitizeError(string? error)
    {
        var value = string.IsNullOrWhiteSpace(error) ? "Unknown delivery error." : error.Trim();
        return value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
    }
}
