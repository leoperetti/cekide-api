using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence.Repositories;

public sealed class PaymentRepository(CekideDbContext context) : IPaymentRepository
{
    public Task<MonthlyPayment?> GetMonthlyAsync(int id, CancellationToken cancellationToken = default) =>
        context.MonthlyPayments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<ShortTermPass?> GetPassAsync(int id, CancellationToken cancellationToken = default) =>
        context.ShortTermPasses.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> PaidMonthlyExistsAsync(int studentId, string period, CancellationToken cancellationToken = default) =>
        context.MonthlyPayments.AnyAsync(
            p => p.StudentId == studentId && p.Period == period && p.Status == PaymentStatus.Paid, cancellationToken);

    public async Task<IReadOnlyList<MonthlyPayment>> ListMonthlyForStudentAsync(int studentId, CancellationToken cancellationToken = default) =>
        await context.MonthlyPayments.Where(p => p.StudentId == studentId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ShortTermPass>> ListPassesForStudentAsync(int studentId, CancellationToken cancellationToken = default) =>
        await context.ShortTermPasses.Where(p => p.StudentId == studentId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MonthlyPayment>> ListMonthlyByPeriodAsync(string period, CancellationToken cancellationToken = default) =>
        await context.MonthlyPayments.Where(p => p.Period == period).ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<int>> PaidStudentIdsAsync(string period, CancellationToken cancellationToken = default) =>
        (await context.MonthlyPayments
            .Where(p => p.Period == period && p.Status == PaymentStatus.Paid)
            .Select(p => p.StudentId)
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<IReadOnlyList<PaymentHistory>> ListHistoryAsync(PaymentKind kind, int paymentId, CancellationToken cancellationToken = default) =>
        await context.PaymentHistory
            .Where(h => h.PaymentKind == kind && h.PaymentId == paymentId)
            .OrderBy(h => h.PerformedAt).ThenBy(h => h.Id)
            .ToListAsync(cancellationToken);

    public void AddMonthly(MonthlyPayment payment) => context.MonthlyPayments.Add(payment);

    public void AddPass(ShortTermPass pass) => context.ShortTermPasses.Add(pass);

    public void AddHistory(PaymentHistory entry) => context.PaymentHistory.Add(entry);
}
