namespace CekideApi.Domain;

public enum MonthlyPaymentStatus
{
    Paid,
    Pending,
    Overdue
}

/// <summary>
/// The derived (never stored) monthly obligation of a student for one period.
/// </summary>
/// <param name="DueFrom">First day on which the month is payable.</param>
/// <param name="OverdueFrom">First day on which an unpaid month is overdue.</param>
/// <param name="OverdueNoticeAllowed">False for the month a student joined the monthly membership after day 10.</param>
public sealed record MonthlyObligation(PaymentPeriod Period, DateOnly DueFrom, DateOnly OverdueFrom, bool OverdueNoticeAllowed);

public static class PaymentRules
{
    /// <summary>Days 1 through 10 are the payment window; overdue starts on day 11.</summary>
    public const int LastDayOfPaymentWindow = 10;

    public static MonthlyObligation? ObligationFor(Student student, PaymentPeriod period)
    {
        // Short-term-only and inactive students never owe a monthly payment.
        if (!student.IsActive || student.MonthlyMembershipStartedOn is not { } startedOn)
        {
            return null;
        }

        var startPeriod = PaymentPeriod.From(startedOn);
        if (period < startPeriod)
        {
            return null;
        }

        if (period == startPeriod)
        {
            return startedOn.Day > LastDayOfPaymentWindow
                ? new MonthlyObligation(period, startedOn, period.Next().FirstDay, OverdueNoticeAllowed: false)
                : new MonthlyObligation(period, startedOn, OverdueDay(period), OverdueNoticeAllowed: true);
        }

        return new MonthlyObligation(period, period.FirstDay, OverdueDay(period), OverdueNoticeAllowed: true);
    }

    public static MonthlyPaymentStatus StatusOf(MonthlyObligation obligation, bool paid, DateOnly today)
    {
        if (paid)
        {
            return MonthlyPaymentStatus.Paid;
        }

        return today >= obligation.OverdueFrom ? MonthlyPaymentStatus.Overdue : MonthlyPaymentStatus.Pending;
    }

    /// <summary>No partial payments: the amount must equal the plan price in force for the period.</summary>
    public static void EnsureAmountMatchesPrice(decimal amount, decimal price)
    {
        if (amount != price)
        {
            throw new DomainValidationException($"The amount must be exactly {price:0.00}; partial or different payments are not accepted.");
        }
    }

    /// <summary>Both ends are inclusive: a one-day pass is valid only on its start day.</summary>
    public static (DateOnly ValidFrom, DateOnly ValidUntil) PassValidity(PassType type, DateOnly validFrom) =>
        (validFrom, validFrom.AddDays(type.DurationDays() - 1));

    private static DateOnly OverdueDay(PaymentPeriod period) => period.FirstDay.AddDays(LastDayOfPaymentWindow);
}
