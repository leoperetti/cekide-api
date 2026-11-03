using System.Text.Json;
using CekideApi.Domain;

namespace CekideApi.Application.Payments;

/// <summary>Send either <c>period</c> (monthly payment) or <c>type</c> (short-term pass). Amount defaults to the price and must equal it.</summary>
public sealed record RegisterPaymentRequest(string? Period, PassType? Type, decimal? Amount, DateOnly? PaidOn, DateOnly? ValidFrom);

/// <summary>Monthly payments accept amount/paid_on; passes accept amount/valid_from/valid_until.</summary>
public sealed record ModifyPaymentRequest(decimal? Amount, DateOnly? PaidOn, DateOnly? ValidFrom, DateOnly? ValidUntil);

public sealed record PaymentDto(
    int Id,
    PaymentKind Kind,
    int StudentId,
    string? Period,
    PassType? Type,
    decimal Amount,
    PaymentStatus Status,
    DateOnly? PaidOn,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    string RegisteredBy,
    DateTime RegisteredAt)
{
    public static PaymentDto From(MonthlyPayment p) =>
        new(p.Id, PaymentKind.Monthly, p.StudentId, p.Period, null, p.Amount, p.Status, p.PaidOn, null, null, p.RegisteredBy, p.RegisteredAt);

    public static PaymentDto From(ShortTermPass p) =>
        new(p.Id, PaymentKind.Pass, p.StudentId, null, p.Type, p.Amount, p.Status, null, p.ValidFrom, p.ValidUntil, p.RegisteredBy, p.RegisteredAt);
}

/// <param name="Status">paid | pending | overdue | voided</param>
public sealed record PaymentRowDto(
    int StudentId, string StudentName, string Period, string Status, decimal Amount, int? PaymentId, DateOnly? PaidOn);

public sealed record PaymentHistoryDto(
    int Id, PaymentAction Action, JsonElement? OldValues, JsonElement? NewValues, string PerformedBy, DateTime PerformedAt);
