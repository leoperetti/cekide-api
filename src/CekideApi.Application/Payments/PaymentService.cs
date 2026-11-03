using System.Text.Json;
using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Payments;

public sealed class PaymentService(
    IPaymentRepository payments,
    IStudentRepository students,
    IPlanRepository plans,
    IPassTariffRepository tariffs,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private static readonly JsonNamingPolicy SnakeCase = JsonNamingPolicy.SnakeCaseLower;

    // ---------- Registration (owner and teacher) ----------

    public async Task<PaymentDto> RegisterAsync(
        int studentId, RegisterPaymentRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var student = await students.GetByIdAsync(studentId, cancellationToken)
                      ?? throw new NotFoundException($"Student {studentId} was not found.");

        if (!student.IsActive)
        {
            throw new DomainValidationException("Inactive students cannot register new payments.");
        }

        if ((request.Period is not null) == (request.Type is not null))
        {
            throw new DomainValidationException("Provide either 'period' (monthly payment) or 'type' (short-term pass).");
        }

        return request.Period is not null
            ? await RegisterMonthlyAsync(student, request, actor, cancellationToken)
            : await RegisterPassAsync(student, request, actor, cancellationToken);
    }

    private async Task<PaymentDto> RegisterMonthlyAsync(
        Student student, RegisterPaymentRequest request, string actor, CancellationToken cancellationToken)
    {
        var period = PaymentPeriod.Parse(request.Period);
        if (!student.HasMonthlyMembership)
        {
            throw new DomainValidationException("The student has no monthly membership (only short-term passes).");
        }

        if (PaymentRules.ObligationFor(student, period) is null)
        {
            throw new DomainValidationException($"The monthly membership started after {period}.");
        }

        var planId = student.PlanIdOn(period.FirstDay)!.Value;
        var plan = await plans.GetByIdAsync(planId, cancellationToken)
                   ?? throw new DomainValidationException($"Plan {planId} does not exist.");

        var amount = request.Amount ?? plan.MonthlyPrice;
        PaymentRules.EnsureAmountMatchesPrice(amount, plan.MonthlyPrice);

        if (await payments.PaidMonthlyExistsAsync(student.Id, period.ToString(), cancellationToken))
        {
            throw new ConflictException($"The student already has a paid monthly payment for {period}.");
        }

        var now = clock.UtcNow.UtcDateTime;
        var payment = MonthlyPayment.Create(student.Id, period, amount, request.PaidOn ?? Today(), actor, now);

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            payments.AddMonthly(payment);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            payments.AddHistory(PaymentHistory.Record(
                PaymentKind.Monthly, payment.Id, PaymentAction.Created, null, Snapshot(payment), actor, now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return PaymentDto.From(payment);
    }

    private async Task<PaymentDto> RegisterPassAsync(
        Student student, RegisterPaymentRequest request, string actor, CancellationToken cancellationToken)
    {
        var type = request.Type!.Value;
        var tariff = await tariffs.GetByTypeAsync(type, cancellationToken)
                     ?? throw new DomainValidationException($"No tariff is configured for the '{TypeName(type)}' pass.");

        var amount = request.Amount ?? tariff.Price;
        PaymentRules.EnsureAmountMatchesPrice(amount, tariff.Price);

        var now = clock.UtcNow.UtcDateTime;
        var pass = ShortTermPass.Create(student.Id, type, amount, request.ValidFrom ?? Today(), actor, now);

        await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            payments.AddPass(pass);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            payments.AddHistory(PaymentHistory.Record(
                PaymentKind.Pass, pass.Id, PaymentAction.Created, null, Snapshot(pass), actor, now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return PaymentDto.From(pass);
    }

    public async Task<IReadOnlyList<PaymentDto>> ListForStudentAsync(int studentId, CancellationToken cancellationToken = default)
    {
        if (await students.GetByIdAsync(studentId, cancellationToken) is null)
        {
            throw new NotFoundException($"Student {studentId} was not found.");
        }

        var monthly = (await payments.ListMonthlyForStudentAsync(studentId, cancellationToken)).Select(PaymentDto.From);
        var passes = (await payments.ListPassesForStudentAsync(studentId, cancellationToken)).Select(PaymentDto.From);
        return monthly.Concat(passes).OrderByDescending(p => p.RegisteredAt).ThenByDescending(p => p.Id).ToList();
    }

    // ---------- Modify / void (owner only, always with history) ----------

    public async Task<PaymentDto> ModifyAsync(
        PaymentKind kind, int paymentId, ModifyPaymentRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow.UtcDateTime;
        if (kind == PaymentKind.Monthly)
        {
            if (request.ValidFrom is not null || request.ValidUntil is not null)
            {
                throw new DomainValidationException("valid_from/valid_until only apply to short-term passes (kind=pass).");
            }

            var payment = await RequireMonthlyAsync(paymentId, cancellationToken);
            var before = Snapshot(payment);
            payment.Modify(request.Amount, request.PaidOn);
            payments.AddHistory(PaymentHistory.Record(kind, payment.Id, PaymentAction.Modified, before, Snapshot(payment), actor, now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return PaymentDto.From(payment);
        }

        if (request.PaidOn is not null)
        {
            throw new DomainValidationException("paid_on only applies to monthly payments.");
        }

        var pass = await RequirePassAsync(paymentId, cancellationToken);
        var passBefore = Snapshot(pass);
        pass.Modify(request.Amount, request.ValidFrom, request.ValidUntil);
        payments.AddHistory(PaymentHistory.Record(kind, pass.Id, PaymentAction.Modified, passBefore, Snapshot(pass), actor, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PaymentDto.From(pass);
    }

    public async Task VoidAsync(PaymentKind kind, int paymentId, string actor, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow.UtcDateTime;
        if (kind == PaymentKind.Monthly)
        {
            var payment = await RequireMonthlyAsync(paymentId, cancellationToken);
            var before = Snapshot(payment);
            payment.Void();
            payments.AddHistory(PaymentHistory.Record(kind, payment.Id, PaymentAction.Voided, before, Snapshot(payment), actor, now));
        }
        else
        {
            var pass = await RequirePassAsync(paymentId, cancellationToken);
            var before = Snapshot(pass);
            pass.Void();
            payments.AddHistory(PaymentHistory.Record(kind, pass.Id, PaymentAction.Voided, before, Snapshot(pass), actor, now));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentHistoryDto>> HistoryAsync(
        PaymentKind kind, int paymentId, CancellationToken cancellationToken = default)
    {
        if (kind == PaymentKind.Monthly)
        {
            await RequireMonthlyAsync(paymentId, cancellationToken);
        }
        else
        {
            await RequirePassAsync(paymentId, cancellationToken);
        }

        return (await payments.ListHistoryAsync(kind, paymentId, cancellationToken))
            .Select(h => new PaymentHistoryDto(h.Id, h.Action, Parse(h.OldValues), Parse(h.NewValues), h.PerformedBy, h.PerformedAt))
            .ToList();
    }

    // ---------- Derived monthly status ----------

    /// <summary>Payments screen (owner): one row per student/period state, optionally filtered by status.</summary>
    public async Task<IReadOnlyList<PaymentRowDto>> ListRowsAsync(
        string? status, string? period, CancellationToken cancellationToken = default)
    {
        var wanted = ParseStatusFilter(status);
        var today = Today();
        var billingPeriod = period is null ? PaymentPeriod.From(today) : PaymentPeriod.Parse(period);

        var allStudents = await students.SearchAsync(null, null, cancellationToken);
        var periodPayments = (await payments.ListMonthlyByPeriodAsync(billingPeriod.ToString(), cancellationToken))
            .ToLookup(p => p.StudentId);
        var planPrices = (await plans.GetByIdsAsync(
                allStudents.Select(s => s.PlanIdOn(billingPeriod.FirstDay)).OfType<int>(), cancellationToken))
            .ToDictionary(p => p.Id, p => p.MonthlyPrice);

        var rows = new List<PaymentRowDto>();
        foreach (var student in allStudents)
        {
            var studentPayments = periodPayments[student.Id].ToList();
            var paid = studentPayments.FirstOrDefault(p => p.Status == PaymentStatus.Paid);

            if (paid is not null)
            {
                rows.Add(new PaymentRowDto(student.Id, student.FullName, billingPeriod.ToString(), "paid", paid.Amount, paid.Id, paid.PaidOn));
            }
            else if (PaymentRules.ObligationFor(student, billingPeriod) is { } obligation)
            {
                var planId = student.PlanIdOn(billingPeriod.FirstDay);
                var price = planId is { } id && planPrices.TryGetValue(id, out var value) ? value : 0m;
                var state = PaymentRules.StatusOf(obligation, paid: false, today);
                rows.Add(new PaymentRowDto(student.Id, student.FullName, billingPeriod.ToString(), StatusName(state), price, null, null));
            }

            rows.AddRange(studentPayments
                .Where(p => p.Status == PaymentStatus.Voided)
                .Select(p => new PaymentRowDto(student.Id, student.FullName, billingPeriod.ToString(), "voided", p.Amount, p.Id, p.PaidOn)));
        }

        return rows
            .Where(r => wanted is null || r.Status == wanted)
            .OrderBy(r => r.StudentName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Status, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Current-month status (paid | pending | overdue) per student; null when there is no monthly obligation.</summary>
    public async Task<IReadOnlyDictionary<int, string?>> CurrentMonthStatusesAsync(
        IReadOnlyCollection<Student> list, CancellationToken cancellationToken = default)
    {
        var today = Today();
        var period = PaymentPeriod.From(today);
        var paidIds = await payments.PaidStudentIdsAsync(period.ToString(), cancellationToken);

        return list.ToDictionary(
            student => student.Id,
            student => PaymentRules.ObligationFor(student, period) is { } obligation
                ? StatusName(PaymentRules.StatusOf(obligation, paidIds.Contains(student.Id), today))
                : null);
    }

    // ---------- Helpers ----------

    private DateOnly Today() => GymCalendar.Today(clock.UtcNow);

    private async Task<MonthlyPayment> RequireMonthlyAsync(int id, CancellationToken cancellationToken) =>
        await payments.GetMonthlyAsync(id, cancellationToken) ?? throw new NotFoundException($"Monthly payment {id} was not found.");

    private async Task<ShortTermPass> RequirePassAsync(int id, CancellationToken cancellationToken) =>
        await payments.GetPassAsync(id, cancellationToken) ?? throw new NotFoundException($"Pass payment {id} was not found.");

    private static string StatusName(MonthlyPaymentStatus status) => status.ToString().ToLowerInvariant();

    private static string TypeName(PassType type) => SnakeCase.ConvertName(type.ToString());

    private static string? ParseStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "paid" or "pending" or "overdue" or "voided"
            ? normalized
            : throw new DomainValidationException("status must be one of: paid, pending, overdue, voided.");
    }

    private static JsonElement? Parse(string? json) =>
        json is null ? null : JsonDocument.Parse(json).RootElement.Clone();

    private static string Snapshot(MonthlyPayment p) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["student_id"] = p.StudentId,
        ["period"] = p.Period,
        ["amount"] = p.Amount,
        ["paid_on"] = p.PaidOn,
        ["status"] = p.Status.ToString().ToLowerInvariant()
    });

    private static string Snapshot(ShortTermPass p) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["student_id"] = p.StudentId,
        ["type"] = TypeName(p.Type),
        ["amount"] = p.Amount,
        ["valid_from"] = p.ValidFrom,
        ["valid_until"] = p.ValidUntil,
        ["status"] = p.Status.ToString().ToLowerInvariant()
    });
}
