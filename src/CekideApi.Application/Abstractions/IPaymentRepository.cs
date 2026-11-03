using CekideApi.Domain;

namespace CekideApi.Application.Abstractions;

public interface IPaymentRepository
{
    Task<MonthlyPayment?> GetMonthlyAsync(int id, CancellationToken cancellationToken = default);

    Task<ShortTermPass?> GetPassAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> PaidMonthlyExistsAsync(int studentId, string period, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonthlyPayment>> ListMonthlyForStudentAsync(int studentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShortTermPass>> ListPassesForStudentAsync(int studentId, CancellationToken cancellationToken = default);

    /// <summary>Every monthly payment (paid or voided) of a period.</summary>
    Task<IReadOnlyList<MonthlyPayment>> ListMonthlyByPeriodAsync(string period, CancellationToken cancellationToken = default);

    /// <summary>Ids of the students that have a Paid monthly payment for the period.</summary>
    Task<IReadOnlySet<int>> PaidStudentIdsAsync(string period, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentHistory>> ListHistoryAsync(PaymentKind kind, int paymentId, CancellationToken cancellationToken = default);

    void AddMonthly(MonthlyPayment payment);

    void AddPass(ShortTermPass pass);

    void AddHistory(PaymentHistory entry);
}
