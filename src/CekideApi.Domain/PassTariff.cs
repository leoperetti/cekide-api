namespace CekideApi.Domain;

public enum PassType
{
    Day = 1,
    Week = 2,
    FifteenDays = 3
}

public static class PassTypeExtensions
{
    public static int DurationDays(this PassType type) => type switch
    {
        PassType.Day => 1,
        PassType.Week => 7,
        PassType.FifteenDays => 15,
        _ => throw new DomainValidationException($"Unknown pass type '{type}'.")
    };
}

public sealed class PassTariff : Entity
{
    private PassTariff()
    {
    }

    public PassType Type { get; private set; }

    public decimal Price { get; private set; }

    public static PassTariff Create(PassType type, decimal price)
    {
        if (!Enum.IsDefined(type))
        {
            throw new DomainValidationException($"Unknown pass type '{(int)type}'.");
        }

        return new PassTariff { Type = type, Price = Money.EnsureValid(price, "Price") };
    }

    public void ChangePrice(decimal price) => Price = Money.EnsureValid(price, "Price");
}
