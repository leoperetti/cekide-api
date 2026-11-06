namespace CekideApi.Domain;

public sealed class KioskSession : Entity
{
    public const int MaxLabelLength = 100;
    public const int MaxTenantLength = 200;
    public const int MaxCreatedByLength = 100;
    public const int MaxRevokedByLength = 100;

    private KioskSession()
    {
    }

    public string Label { get; private set; } = string.Empty;

    public string Tenant { get; private set; } = string.Empty;

    public string CreatedBy { get; private set; } = string.Empty;

    public string? ActivationCodeHash { get; private set; }

    public DateTime ActivationExpiresAt { get; private set; }

    public DateTime? ActivatedAt { get; private set; }

    public string? TokenHash { get; private set; }

    public DateTime? ExpiresAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public string? RevokedBy { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive(DateTimeOffset utcNow) =>
        ActivatedAt is not null &&
        ExpiresAt > utcNow.UtcDateTime &&
        RevokedAt is null &&
        TokenHash is not null;

    public static KioskSession Create(
        string? label,
        string? tenant,
        string? createdBy,
        string? activationCodeHash,
        DateTimeOffset utcNow)
    {
        var normalizedLabel = label?.Trim();
        var normalizedTenant = tenant?.Trim();
        var normalizedCreator = createdBy?.Trim();
        var normalizedHash = activationCodeHash?.Trim();

        if (string.IsNullOrEmpty(normalizedLabel) || normalizedLabel.Length > MaxLabelLength)
        {
            throw new DomainValidationException($"Kiosk label is required and cannot exceed {MaxLabelLength} characters.");
        }

        if (string.IsNullOrEmpty(normalizedTenant) || normalizedTenant.Length > MaxTenantLength)
        {
            throw new DomainValidationException($"Kiosk tenant is required and cannot exceed {MaxTenantLength} characters.");
        }

        if (string.IsNullOrEmpty(normalizedCreator) || normalizedCreator.Length > MaxCreatedByLength)
        {
            throw new DomainValidationException($"Kiosk creator is required and cannot exceed {MaxCreatedByLength} characters.");
        }

        if (string.IsNullOrEmpty(normalizedHash))
        {
            throw new DomainValidationException("Kiosk activation code hash is required.");
        }

        var now = utcNow.ToUniversalTime().UtcDateTime;
        if (utcNow == default)
        {
            throw new DomainValidationException("Kiosk creation time is required.");
        }

        return new KioskSession
        {
            Label = normalizedLabel,
            Tenant = normalizedTenant,
            CreatedBy = normalizedCreator,
            ActivationCodeHash = normalizedHash,
            ActivationExpiresAt = now.AddMinutes(10)
        };
    }

    public void Activate(string? activationCodeHash, string? tokenHash, DateTimeOffset utcNow)
    {
        var now = utcNow.ToUniversalTime().UtcDateTime;
        if (ActivatedAt is not null || RevokedAt is not null ||
            ActivationExpiresAt <= now ||
            string.IsNullOrWhiteSpace(activationCodeHash) ||
            !string.Equals(ActivationCodeHash, activationCodeHash, StringComparison.Ordinal))
        {
            throw new ConflictException("Kiosk activation code is invalid or expired.");
        }

        var normalizedTokenHash = tokenHash?.Trim();
        if (string.IsNullOrEmpty(normalizedTokenHash))
        {
            throw new DomainValidationException("Kiosk token hash is required.");
        }

        ActivatedAt = now;
        TokenHash = normalizedTokenHash;
        ExpiresAt = now.AddHours(12);
        ActivationCodeHash = null;
    }

    public void Revoke(string? revokedBy, DateTimeOffset utcNow)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        var normalizedActor = revokedBy?.Trim();
        if (string.IsNullOrEmpty(normalizedActor) || normalizedActor.Length > MaxRevokedByLength)
        {
            throw new DomainValidationException($"Kiosk revoking subject is required and cannot exceed {MaxRevokedByLength} characters.");
        }

        if (utcNow == default)
        {
            throw new DomainValidationException("Kiosk revocation time is required.");
        }

        RevokedAt = utcNow.ToUniversalTime().UtcDateTime;
        RevokedBy = normalizedActor;
    }

    public bool HasValidTokenHash(string? tokenHash, DateTimeOffset utcNow) =>
        IsActive(utcNow) &&
        !string.IsNullOrWhiteSpace(tokenHash) &&
        string.Equals(TokenHash, tokenHash, StringComparison.Ordinal);
}
