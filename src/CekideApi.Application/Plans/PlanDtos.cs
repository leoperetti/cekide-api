using CekideApi.Domain;

namespace CekideApi.Application.Plans;

public sealed record CreatePlanRequest(string? Name, decimal? MonthlyPrice);

public sealed record UpdatePlanRequest(string? Name, decimal? MonthlyPrice, bool? IsActive);

public sealed record PassTariffRequest(PassType? Type, decimal? Price);

public sealed record PlanDto(int Id, string Name, decimal MonthlyPrice, bool IsActive)
{
    public static PlanDto From(Plan plan) => new(plan.Id, plan.Name, plan.MonthlyPrice, plan.IsActive);
}

public sealed record PassTariffDto(PassType Type, decimal Price)
{
    public static PassTariffDto From(PassTariff tariff) => new(tariff.Type, tariff.Price);
}
