using System.Globalization;
using System.Text.RegularExpressions;

namespace CekideApi.Domain;

/// <summary>A calendar month in YYYY-MM form (the billing period of a monthly payment).</summary>
public readonly partial record struct PaymentPeriod : IComparable<PaymentPeriod>
{
    [GeneratedRegex(@"^(\d{4})-(0[1-9]|1[0-2])$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public PaymentPeriod(int year, int month)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12)
        {
            throw new DomainValidationException("Period must be a valid year and month.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    public static PaymentPeriod Parse(string? value)
    {
        var match = value is null ? null : Pattern().Match(value);
        if (match is not { Success: true })
        {
            throw new DomainValidationException("Period must use the format YYYY-MM (for example 2026-11).");
        }

        return new PaymentPeriod(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    public static PaymentPeriod From(DateOnly day) => new(day.Year, day.Month);

    public PaymentPeriod Next() => From(FirstDay.AddMonths(1));

    public int CompareTo(PaymentPeriod other) => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);

    public override string ToString() => $"{Year:0000}-{Month:00}";

    public static bool operator <(PaymentPeriod left, PaymentPeriod right) => left.CompareTo(right) < 0;

    public static bool operator >(PaymentPeriod left, PaymentPeriod right) => left.CompareTo(right) > 0;

    public static bool operator <=(PaymentPeriod left, PaymentPeriod right) => left.CompareTo(right) <= 0;

    public static bool operator >=(PaymentPeriod left, PaymentPeriod right) => left.CompareTo(right) >= 0;
}
