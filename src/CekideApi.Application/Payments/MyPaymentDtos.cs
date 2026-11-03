using CekideApi.Domain;

namespace CekideApi.Application.Payments;

/// <summary>One row of the student's own payment screen: a monthly obligation (paid, pending or overdue) or a short-term pass.</summary>
/// <param name="Status">paid | pending | overdue</param>
public sealed record MyPaymentDto(
    int? Id,
    PaymentKind Kind,
    string? Period,
    PassType? Type,
    decimal Amount,
    string Status,
    DateOnly? PaidOn,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil);
