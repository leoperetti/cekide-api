namespace CekideApi.Domain;

public sealed class ShortTermPass : Entity
{
    private ShortTermPass()
    {
    }

    public int StudentId { get; private set; }

    public PassType Type { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly ValidUntil { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string RegisteredBy { get; private set; } = string.Empty;

    public DateTime RegisteredAt { get; private set; }

    public static ShortTermPass Create(
        int studentId, PassType type, decimal amount, DateOnly validFrom, string registeredBy, DateTime registeredAtUtc)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainValidationException($"Unknown pass type '{(int)type}'.");
        }

        var (from, until) = PaymentRules.PassValidity(type, validFrom);
        return new ShortTermPass
        {
            StudentId = studentId,
            Type = type,
            Amount = Money.EnsureValid(amount, "Amount"),
            ValidFrom = from,
            ValidUntil = until,
            Status = PaymentStatus.Paid,
            RegisteredBy = registeredBy,
            RegisteredAt = registeredAtUtc
        };
    }

    public void Modify(decimal? amount, DateOnly? validFrom, DateOnly? validUntil)
    {
        EnsurePaid();
        if (amount is null && validFrom is null && validUntil is null)
        {
            throw new DomainValidationException("Nothing to modify: provide an amount or validity dates.");
        }

        var newFrom = validFrom ?? ValidFrom;
        var newUntil = validUntil ?? ValidUntil;
        if (newUntil < newFrom)
        {
            throw new DomainValidationException("valid_until cannot be before valid_from.");
        }

        Amount = amount is { } value ? Money.EnsureValid(value, "Amount") : Amount;
        ValidFrom = newFrom;
        ValidUntil = newUntil;
    }

    public void Void()
    {
        EnsurePaid();
        Status = PaymentStatus.Voided;
    }

    private void EnsurePaid()
    {
        if (Status != PaymentStatus.Paid)
        {
            throw new ConflictException("The payment has been voided.");
        }
    }
}
