using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence;

public sealed class UnitOfWork(CekideDbContext context) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The record was modified concurrently.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            foreach (var student in exception.Entries.Select(e => e.Entity).OfType<Student>())
            {
                if (student.Dni is not null && await context.Students.AsNoTracking()
                    .AnyAsync(s => s.Dni == student.Dni && s.Id != student.Id, cancellationToken))
                {
                    throw new AttendanceException(409, "dni_already_exists", "Another student already uses this DNI.");
                }
            }

            throw new ConflictException("The record conflicts with an existing one.");
        }
    }

    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await action();
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        Microsoft.Data.SqlClient.SqlException sql => sql.Number is 2601 or 2627,
        { } inner => inner.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

}
