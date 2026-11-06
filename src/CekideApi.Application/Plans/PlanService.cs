using CekideApi.Application.Abstractions;
using CekideApi.Application.Recoveries;
using CekideApi.Domain;

namespace CekideApi.Application.Plans;

public sealed class PlanService(
    IPlanRepository plans,
    IPassTariffRepository tariffs,
    IUnitOfWork unitOfWork,
    IClock clock,
    RecoveryAccrualService recoveryAccrual)
{
    public async Task<IReadOnlyList<PlanDto>> ListAsync(CancellationToken cancellationToken = default) =>
        await MapAsync(await plans.ListAsync(cancellationToken), cancellationToken);

    public async Task<PlanDto> CreateAsync(CreatePlanRequest request, string? configuredBy, CancellationToken cancellationToken = default)
    {
        var limits = request.ReadLimits();
        var plan = Plan.Create(request.Name, RequirePrice(request.MonthlyPrice));
        if (await plans.NameExistsAsync(plan.Name, null, cancellationToken))
        {
            throw new ConflictException($"A plan named '{plan.Name}' already exists.");
        }

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            plans.Add(plan);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (limits is { } values)
            {
                plans.AddAttendanceLimits(PlanAttendanceLimits.CreateInitial(
                    plan.Id, values.Weekly, values.Monthly, configuredBy, Today()));
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }, cancellationToken);
        if (limits is not null)
        {
            await recoveryAccrual.CaptureCurrentWeekForPlanAsync(plan.Id, cancellationToken);
        }

        return (await MapAsync([plan], cancellationToken)).Single();
    }

    public async Task<PlanDto> UpdateAsync(int planId, UpdatePlanRequest request, string? configuredBy, CancellationToken cancellationToken = default)
    {
        var limits = request.ReadLimits();
        Plan? plan = null;
        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            plan = await plans.GetByIdAsync(planId, cancellationToken)
                   ?? throw new NotFoundException($"Plan {planId} was not found.");

            plan.Update(request.Name, RequirePrice(request.MonthlyPrice));
            if (await plans.NameExistsAsync(plan.Name, plan.Id, cancellationToken))
            {
                throw new ConflictException($"A plan named '{plan.Name}' already exists.");
            }

            if (request.IsActive is { } isActive)
            {
                if (isActive) { plan.Activate(); }
                else { plan.Deactivate(); }
            }

            if (limits is { } values)
            {
                var today = Today();
                var existing = await plans.GetAttendanceLimitsAsync([plan.Id], cancellationToken);
                if (existing.Count == 0)
                {
                    plans.AddAttendanceLimits(PlanAttendanceLimits.CreateInitial(
                        plan.Id, values.Weekly, values.Monthly, configuredBy, today));
                }
                else
                {
                    var effectiveFrom = GymCalendar.MonthEndExclusive(today);
                    var pending = existing.SingleOrDefault(l => l.EffectiveFrom == effectiveFrom);
                    if (pending is not null)
                    {
                        pending.UpdatePending(values.Weekly, values.Monthly, configuredBy, today);
                    }
                    else
                    {
                        plans.AddAttendanceLimits(PlanAttendanceLimits.CreateRevision(
                            plan.Id, values.Weekly, values.Monthly, configuredBy, today));
                    }
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        if (limits is not null && plan is not null)
        {
            await recoveryAccrual.CaptureCurrentWeekForPlanAsync(plan.Id, cancellationToken);
        }

        return (await MapAsync([plan ?? throw new InvalidOperationException("Plan update did not execute.")], cancellationToken)).Single();
    }

    private DateOnly Today() => GymCalendar.Today(clock.UtcNow);

    private async Task<IReadOnlyList<PlanDto>> MapAsync(IReadOnlyList<Plan> list, CancellationToken cancellationToken)
    {
        var today = Today();
        var limits = await plans.GetAttendanceLimitsAsync(list.Select(p => p.Id), cancellationToken);
        return list.Select(plan =>
        {
            var versions = limits.Where(l => l.PlanId == plan.Id).ToList();
            var current = versions.Where(l => l.EffectiveFrom <= today).MaxBy(l => l.EffectiveFrom);
            var pending = versions.Where(l => l.EffectiveFrom > today).MinBy(l => l.EffectiveFrom);
            return PlanDto.From(plan, current, pending);
        }).ToList();
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
