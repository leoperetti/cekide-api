using CekideApi.Application.Abstractions;
using CekideApi.Domain;

namespace CekideApi.Application.Recoveries;

public sealed class RecoveryStateService(
    IRecoveryRepository recoveries,
    IAttendanceTransaction transaction,
    IClock clock)
{
    public async Task<int> NormalizeStudentAsync(int studentId, CancellationToken cancellationToken = default)
    {
        if (studentId <= 0)
        {
            throw new DomainValidationException("Student id must be positive.");
        }

        var now = clock.UtcNow;
        var today = GymCalendar.Today(now);
        return await transaction.ExecuteForStudentAsync(studentId, async token =>
        {
            var changed = 0;
            var reservations = await recoveries.GetReservationsToNormalizeAsync(studentId, today, token);
            foreach (var state in reservations)
            {
                if (state.Shift.Status == GymShiftStatus.Cancelled)
                {
                    state.Reservation.Cancel(RecoveryReservationCancelReason.ShiftCancelled, now);
                }
                else if (!state.Shift.IsFinished(now))
                {
                    continue;
                }
                else
                {
                    state.Reservation.MarkMissed(now);
                }

                state.Credit.Release(today);
                changed++;
            }

            var expiringCredits = await recoveries.GetCreditsToExpireAsync(studentId, now, token);
            foreach (var credit in expiringCredits)
            {
                var previousStatus = credit.Status;
                credit.Expire(now);
                if (credit.Status != previousStatus)
                {
                    changed++;
                }
            }

            return changed;
        }, cancellationToken);
    }
}
