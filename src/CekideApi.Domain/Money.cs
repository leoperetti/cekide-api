namespace CekideApi.Domain;

/// <summary>Money rules shared by plans, tariffs and payments: decimal(18,2), strictly positive.</summary>
public static class Money
{
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public static decimal EnsureValid(decimal amount, string field)
    {
        if (amount <= 0)
        {
            throw new DomainValidationException($"{field} must be greater than zero.");
        }

        if (amount != decimal.Round(amount, 2) || amount > MaxAmount)
        {
            throw new DomainValidationException($"{field} must have at most two decimals and fit in decimal(18,2).");
        }

        return decimal.Round(amount, 2);
    }
}
