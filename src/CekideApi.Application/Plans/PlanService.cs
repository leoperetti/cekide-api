using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Plans;

public sealed class PlanService(IPlanRepository plans, IPassTariffRepository tariffs, IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<PlanDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await plans.ListAsync(cancellationToken)).Select(PlanDto.From).ToList();

    public async Task<PlanDto> CreateAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
    {
        var plan = Plan.Create(request.Name, RequirePrice(request.MonthlyPrice));
        if (await plans.NameExistsAsync(plan.Name, null, cancellationToken))
        {
            throw new ConflictException($"A plan named '{plan.Name}' already exists.");
        }

        plans.Add(plan);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PlanDto.From(plan);
    }

    public async Task<PlanDto> UpdateAsync(int planId, UpdatePlanRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await plans.GetByIdAsync(planId, cancellationToken)
                   ?? throw new NotFoundException($"Plan {planId} was not found.");

        plan.Update(request.Name, RequirePrice(request.MonthlyPrice));
        if (await plans.NameExistsAsync(plan.Name, plan.Id, cancellationToken))
        {
            throw new ConflictException($"A plan named '{plan.Name}' already exists.");
        }

        if (request.IsActive is { } isActive)
        {
            if (isActive)
            {
                plan.Activate();
            }
            else
            {
                plan.Deactivate();
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PlanDto.From(plan);
    }

    public async Task<IReadOnlyList<PassTariffDto>> ListTariffsAsync(CancellationToken cancellationToken = default) =>
        (await tariffs.ListAsync(cancellationToken)).Select(PassTariffDto.From).OrderBy(t => t.Type).ToList();

    /// <summary>Creates or updates the tariff of each pass type included in the request.</summary>
    public async Task<IReadOnlyList<PassTariffDto>> SetTariffsAsync(
        IReadOnlyCollection<PassTariffRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0)
        {
            throw new DomainValidationException("At least one tariff is required.");
        }

        var types = requests.Select(r => r.Type ?? throw new DomainValidationException("Tariff type is required.")).ToList();
        if (types.Distinct().Count() != types.Count)
        {
            throw new DomainValidationException("Each pass type can appear only once.");
        }

        foreach (var request in requests)
        {
            var price = RequirePrice(request.Price);
            var existing = await tariffs.GetByTypeAsync(request.Type!.Value, cancellationToken);
            if (existing is null)
            {
                tariffs.Add(PassTariff.Create(request.Type.Value, price));
            }
            else
            {
                existing.ChangePrice(price);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ListTariffsAsync(cancellationToken);
    }

    private static decimal RequirePrice(decimal? price) =>
        price ?? throw new DomainValidationException("Price is required.");
}
