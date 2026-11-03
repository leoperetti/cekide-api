using CekideApi.Application.Abstractions;
using CekideApi.Application.Students;
using CekideApi.Domain;

namespace CekideApi.Application.Payments;

/// <summary>The payments of the signed-in student (the account is linked to its student on first use).</summary>
public sealed class MyPaymentsService(
    StudentLinkingService linking,
    IPaymentRepository payments,
    IPlanRepository plans,
    IClock clock)
{
    public async Task<IReadOnlyList<MyPaymentDto>> ListAsync(
        string? identityId, string? email, CancellationToken cancellationToken = default)
    {
        var link = await linking.TryLinkAsync(identityId, email, cancellationToken);
        var student = link.Outcome switch
        {
            LinkOutcome.Linked or LinkOutcome.AlreadyLinked => link.Student!,
            LinkOutcome.Inactive => throw new ForbiddenException("The student is inactive."),
            _ => throw new NotFoundException("This account is not linked to a student.")
        };

        if (!student.IsActive)
        {
            throw new ForbiddenException("The student is inactive.");
        }

        var today = GymCalendar.Today(clock.UtcNow);
        var rows = await MonthlyRowsAsync(student, today, cancellationToken);
        rows.AddRange(await PassRowsAsync(student, cancellationToken));
        return rows;
    }

    private async Task<List<MyPaymentDto>> MonthlyRowsAsync(Student student, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = new List<MyPaymentDto>();
        if (student.MonthlyMembershipStartedOn is not { } startedOn)
        {
            return rows;
        }

        var paid = (await payments.ListMonthlyForStudentAsync(student.Id, cancellationToken))
            .Where(p => p.Status == PaymentStatus.Paid)
            .ToDictionary(p => p.Period);
        var planIds = new[] { student.PlanId, student.PendingPlanId }.OfType<int>();
        var prices = (await plans.GetByIdsAsync(planIds, cancellationToken)).ToDictionary(p => p.Id, p => p.MonthlyPrice);

        var current = PaymentPeriod.From(today);
        for (var period = PaymentPeriod.From(startedOn); period <= current; period = period.Next())
        {
            if (paid.TryGetValue(period.ToString(), out var payment))
            {
                rows.Add(new MyPaymentDto(
                    payment.Id, PaymentKind.Monthly, payment.Period, null, payment.Amount, "paid", payment.PaidOn, null, null));
            }
            else if (PaymentRules.ObligationFor(student, period) is { } obligation)
            {
                var planId = student.PlanIdOn(period.FirstDay);
                var price = planId is { } id && prices.TryGetValue(id, out var value) ? value : 0m;
                var status = PaymentRules.StatusOf(obligation, paid: false, today).ToString().ToLowerInvariant();
                rows.Add(new MyPaymentDto(null, PaymentKind.Monthly, period.ToString(), null, price, status, null, null, null));
            }
        }

        rows.Reverse();
        return rows;
    }

    private async Task<IEnumerable<MyPaymentDto>> PassRowsAsync(Student student, CancellationToken cancellationToken) =>
        (await payments.ListPassesForStudentAsync(student.Id, cancellationToken))
            .Where(p => p.Status == PaymentStatus.Paid)
            .OrderByDescending(p => p.ValidFrom).ThenByDescending(p => p.Id)
            .Select(p => new MyPaymentDto(p.Id, PaymentKind.Pass, null, p.Type, p.Amount, "paid", null, p.ValidFrom, p.ValidUntil));
}
