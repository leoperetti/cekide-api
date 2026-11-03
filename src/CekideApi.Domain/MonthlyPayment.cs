namespace CekideApi.Domain;

public sealed class MonthlyPayment : Entity
{
    private MonthlyPayment()
    {
    }

    public int StudentId { get; private set; }

    /// <summary>Billing month as YYYY-MM.</summary>
    public string Period { get; private set; } = string.Empty;

    /// <summary>Snapshot of the plan price when the payment was registered.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateOnly PaidOn { get; private set; }

    public string RegisteredBy { get; private set; } = string.Empty;

    public DateTime RegisteredAt { get; private set; }

    public static MonthlyPayment Create(
        int studentId, PaymentPeriod period, decimal amount, DateOnly paidOn, string registeredBy, DateTime registeredAtUtc) =>
        new()
        {
            StudentId = studentId,
            Period = period.ToString(),
            Amount = Money.EnsureValid(amount, "Amount"),
            Status = PaymentStatus.Paid,
            PaidOn = paidOn,
            RegisteredBy = registeredBy,
            RegisteredAt = registeredAtUtc
        };

    public void Modify(decimal? amount, DateOnly? paidOn)
    {
        EnsurePaid();
        if (amount is null && paidOn is null)
        {
            throw new DomainValidationException("Nothing to modify: provide an amount or a payment date.");
        }

        var newAmount = amount is { } value ? Money.EnsureValid(value, "Amount") : Amount;
        Amount = newAmount;
        PaidOn = paidOn ?? PaidOn;
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
