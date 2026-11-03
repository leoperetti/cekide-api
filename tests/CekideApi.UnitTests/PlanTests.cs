using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class PlanTests
{
    [Fact]
    public void Create_trims_the_name_and_keeps_the_price()
    {
        var plan = Plan.Create("  Musculación  ", 15000m);

        Assert.Equal("Musculación", plan.Name);
        Assert.Equal(15000m, plan.MonthlyPrice);
        Assert.True(plan.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_name(string? name)
    {
        Assert.Throws<DomainValidationException>(() => Plan.Create(name, 100m));
    }

    [Fact]
    public void Create_rejects_names_longer_than_100_characters()
    {
        Assert.Throws<DomainValidationException>(() => Plan.Create(new string('a', 101), 100m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Create_rejects_non_positive_prices(double price)
    {
        Assert.Throws<DomainValidationException>(() => Plan.Create("Plan", (decimal)price));
    }

    [Fact]
    public void Create_rejects_prices_with_more_than_two_decimals()
    {
        Assert.Throws<DomainValidationException>(() => Plan.Create("Plan", 10.005m));
    }

    [Fact]
    public void Create_rejects_prices_beyond_decimal_18_2()
    {
        Assert.Throws<DomainValidationException>(() => Plan.Create("Plan", 10_000_000_000_000_000m));
    }

    [Fact]
    public void Update_changes_name_and_price_with_the_same_rules()
    {
        var plan = Plan.Create("Plan", 100m);

        plan.Update(" Premium ", 250.50m);

        Assert.Equal("Premium", plan.Name);
        Assert.Equal(250.50m, plan.MonthlyPrice);
        Assert.Throws<DomainValidationException>(() => plan.Update("Premium", 0m));
    }

    [Fact]
    public void Deactivate_and_activate_toggle_the_plan()
    {
        var plan = Plan.Create("Plan", 100m);

        plan.Deactivate();
        Assert.False(plan.IsActive);

        plan.Activate();
        Assert.True(plan.IsActive);
    }
}

public class PassTariffTests
{
    [Theory]
    [InlineData(PassType.Day)]
    [InlineData(PassType.Week)]
    [InlineData(PassType.FifteenDays)]
    public void Create_accepts_every_short_term_pass_type(PassType type)
    {
        var tariff = PassTariff.Create(type, 2500m);

        Assert.Equal(type, tariff.Type);
        Assert.Equal(2500m, tariff.Price);
    }

    [Fact]
    public void Create_rejects_undefined_types()
    {
        Assert.Throws<DomainValidationException>(() => PassTariff.Create((PassType)99, 100m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_rejects_non_positive_prices(double price)
    {
        Assert.Throws<DomainValidationException>(() => PassTariff.Create(PassType.Day, (decimal)price));
    }

    [Fact]
    public void ChangePrice_validates_the_new_price()
    {
        var tariff = PassTariff.Create(PassType.Week, 100m);

        tariff.ChangePrice(180m);

        Assert.Equal(180m, tariff.Price);
        Assert.Throws<DomainValidationException>(() => tariff.ChangePrice(0m));
    }

    [Theory]
    [InlineData(PassType.Day, 1)]
    [InlineData(PassType.Week, 7)]
    [InlineData(PassType.FifteenDays, 15)]
    public void Each_type_has_its_duration_in_days(PassType type, int days)
    {
        Assert.Equal(days, type.DurationDays());
    }
}
