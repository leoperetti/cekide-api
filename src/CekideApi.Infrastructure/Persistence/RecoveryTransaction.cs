using System.Data;
using System.Globalization;
using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CekideApi.Infrastructure.Persistence;

public sealed class RecoveryTransaction(CekideDbContext context) : IRecoveryTransaction
{
    private const int AppLockTimeoutMilliseconds = 5_000;

    public async Task<T> ExecuteForStudentAndShiftAsync<T>(
        int studentId,
        int shiftId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        if (studentId <= 0 || shiftId <= 0)
        {
            throw new DomainValidationException("Student and shift ids must be positive.");
        }

        ArgumentNullException.ThrowIfNull(action);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var student = context.Database.IsSqlServer()
            ? await context.Students.FromSqlInterpolated(
                    $"SELECT * FROM students WITH (UPDLOCK, HOLDLOCK) WHERE id = {studentId}")
                .SingleOrDefaultAsync(cancellationToken)
            : await context.Students.SingleOrDefaultAsync(candidate => candidate.Id == studentId, cancellationToken);
        if (student is null)
        {
            throw new NotFoundException("Student not found.");
        }

        await AcquireShiftLockAsync(shiftId, transaction, cancellationToken);
        var shiftExists = await context.GymShifts.AnyAsync(shift => shift.Id == shiftId, cancellationToken);
        if (!shiftExists)
        {
            throw new NotFoundException($"Shift {shiftId} was not found.");
        }

        var result = await action(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task AcquireShiftLockAsync(
        int shiftId,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlServer())
        {
            return;
        }

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandType = CommandType.Text;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @timeout;
            SELECT @result;
            """;
        var resource = command.CreateParameter();
        resource.ParameterName = "@resource";
        resource.Value = $"cekide:shift:{shiftId}";
        command.Parameters.Add(resource);
        var timeout = command.CreateParameter();
        timeout.ParameterName = "@timeout";
        timeout.Value = AppLockTimeoutMilliseconds;
        command.Parameters.Add(timeout);

        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new AttendanceException(503, "shift_lock_unavailable", "The shift is busy; retry the operation.");
        }
    }
}
