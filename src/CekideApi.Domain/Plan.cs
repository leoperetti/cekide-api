namespace CekideApi.Domain;

public sealed class Plan : Entity
{
    public const int MaxNameLength = 100;

    private Plan()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public decimal MonthlyPrice { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Plan Create(string? name, decimal monthlyPrice)
    {
        var plan = new Plan();
        plan.Update(name, monthlyPrice);
        return plan;
    }

    public void Update(string? name, decimal monthlyPrice)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainValidationException("Plan name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainValidationException($"Plan name cannot exceed {MaxNameLength} characters.");
        }

        MonthlyPrice = Money.EnsureValid(monthlyPrice, "Monthly price");
        Name = trimmed;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
