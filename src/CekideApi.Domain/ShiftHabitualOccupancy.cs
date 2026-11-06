namespace CekideApi.Domain;

public enum ShiftHabitualOccupancyStatus
{
    Active,
    Removed
}

public sealed class ShiftHabitualOccupancy : Entity
{
    public const int MaxActorLength = 100;

    private ShiftHabitualOccupancy()
    {
    }

    public int ShiftId { get; private set; }

    public int StudentId { get; private set; }

    public DateOnly LocalDate { get; private set; }

    public ShiftHabitualOccupancyStatus Status { get; private set; }

    public string RegisteredBy { get; private set; } = string.Empty;

    public string? RemovedBy { get; private set; }

    public DateTime? RemovedAt { get; private set; }

    public static ShiftHabitualOccupancy Create(
        int shiftId,
        int studentId,
        DateOnly localDate,
        string? registeredBy,
        DateTimeOffset now)
    {
        if (shiftId <= 0 || studentId <= 0 || localDate == default || now == default)
        {
            throw new DomainValidationException("A shift, student, date, and registration time are required.");
        }

        return new ShiftHabitualOccupancy
        {
            ShiftId = shiftId,
            StudentId = studentId,
            LocalDate = localDate,
            Status = ShiftHabitualOccupancyStatus.Active,
            RegisteredBy = NormalizeActor(registeredBy)
        };
    }

    public void Remove(string? actor, DateTimeOffset now)
    {
        if (Status == ShiftHabitualOccupancyStatus.Removed)
        {
            return;
        }

        if (now == default)
        {
            throw new DomainValidationException("An occupancy removal time is required.");
        }

        Status = ShiftHabitualOccupancyStatus.Removed;
        RemovedBy = NormalizeActor(actor);
        RemovedAt = now.UtcDateTime;
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
