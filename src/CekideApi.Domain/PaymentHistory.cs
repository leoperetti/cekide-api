namespace CekideApi.Domain;

/// <summary>Append-only audit trail of changes made to payments.</summary>
public sealed class PaymentHistory : Entity
{
    private PaymentHistory()
    {
    }

    public PaymentKind PaymentKind { get; private set; }

    public int PaymentId { get; private set; }

    public PaymentAction Action { get; private set; }

    /// <summary>JSON snapshot before the change (null for Created).</summary>
    public string? OldValues { get; private set; }

    /// <summary>JSON snapshot after the change.</summary>
    public string? NewValues { get; private set; }

    public string PerformedBy { get; private set; } = string.Empty;

    public DateTime PerformedAt { get; private set; }

    public static PaymentHistory Record(
        PaymentKind kind, int paymentId, PaymentAction action, string? oldValues, string? newValues,
        string performedBy, DateTime performedAtUtc) =>
        new()
        {
            PaymentKind = kind,
            PaymentId = paymentId,
            Action = action,
            OldValues = oldValues,
            NewValues = newValues,
            PerformedBy = performedBy,
            PerformedAt = performedAtUtc
        };
}
