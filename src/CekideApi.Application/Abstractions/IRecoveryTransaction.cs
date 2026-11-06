namespace CekideApi.Application.Abstractions;

public interface IRecoveryTransaction
{
    Task<T> ExecuteForStudentAndShiftAsync<T>(
        int studentId,
        int shiftId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);
}
