namespace CekideApi.Domain;

public enum GymShiftStatus
{
    Open,
    Cancelled
}

public sealed class GymShift : Entity
{
    public const int CapacityPerShift = 12;
    public const int MaxActorLength = 100;

    private GymShift()
    {
    }

    public DateOnly LocalDate { get; private set; }

    public TimeOnly StartsAt { get; private set; }

    public TimeOnly EndsAt { get; private set; }

    public int Capacity { get; private set; } = CapacityPerShift;

    public GymShiftStatus Status { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;

    public string? CancelledBy { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public static GymShift Create(DateOnly localDate, TimeOnly startsAt, string? createdBy, DateTimeOffset now)
    {
        var actor = NormalizeActor(createdBy);
        if (localDate == default || !GymCalendar.IsRecoveryWeekday(localDate))
        {
            throw new DomainValidationException("A shift must be scheduled on a weekday.");
        }

        if (startsAt.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new DomainValidationException("A shift start time must have minute precision.");
        }

        var endsAt = startsAt.AddHours(1);
        if (endsAt <= startsAt)
        {
            throw new DomainValidationException("A shift must end within the same local date.");
        }

        if (now == default)
        {
            throw new DomainValidationException("A shift creation time is required.");
        }

        return new GymShift
        {
            LocalDate = localDate,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Status = GymShiftStatus.Open,
            CreatedBy = actor
        };
    }

    public bool IsFinished(DateTimeOffset utcNow)
    {
        var localNow = TimeZoneInfo.ConvertTime(utcNow, GymCalendar.TimeZone);
        var localDate = DateOnly.FromDateTime(localNow.DateTime);
        return localDate > LocalDate || (localDate == LocalDate && TimeOnly.FromDateTime(localNow.DateTime) >= EndsAt);
    }

    public void Cancel(string? actor, DateTimeOffset now)
    {
        if (Status == GymShiftStatus.Cancelled)
        {
            return;
        }

        var cancellingActor = NormalizeActor(actor);
        if (now == default)
        {
            throw new DomainValidationException("A shift cancellation time is required.");
        }

        Status = GymShiftStatus.Cancelled;
        CancelledBy = cancellingActor;
        CancelledAt = now.UtcDateTime;
    }

    private static string NormalizeActor(string? actor)
    {
        var normalized = actor?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxActorLength)
        {
            throw new DomainValidationException($"A staff subject is required and cannot exceed {MaxActorLength} characters.");
        }

        return normalized;
    }
}
