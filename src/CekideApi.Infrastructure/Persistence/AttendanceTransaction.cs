using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.Infrastructure.Persistence;

public sealed class AttendanceTransaction(CekideDbContext context) : IAttendanceTransaction
{
    private const int MaxAttempts = 3;

    public async Task<T> ExecuteForStudentAsync<T>(
        int studentId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        if (studentId <= 0)
        {
            throw new DomainValidationException("Student id must be positive.");
        }

        ArgumentNullException.ThrowIfNull(action);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(studentId, action, cancellationToken);
            }
            catch (Exception exception) when (attempt < MaxAttempts && IsTransient(exception))
            {
                context.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    private async Task<T> ExecuteOnceAsync<T>(
        int studentId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Student? student;
        if (context.Database.IsSqlServer())
        {
            student = await context.Students
                .FromSqlInterpolated($"SELECT * FROM students WITH (UPDLOCK, HOLDLOCK) WHERE id = {studentId}")
                .SingleOrDefaultAsync(cancellationToken);
        }
        else
        {
            student = await context.Students.SingleOrDefaultAsync(
                candidate => candidate.Id == studentId,
                cancellationToken);
        }

        if (student is null)
        {
            throw new NotFoundException("Student not found.");
        }

        var result = await action(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException &&
                sqlException.Errors.Cast<SqlError>().Any(error =>
                    error.Number is 1205 or 4060 or 10928 or 10929 or 40197 or 40501 or 40613 or 49918 or 49919 or 49920))
            {
                return true;
            }
        }

        return false;
    }
}
