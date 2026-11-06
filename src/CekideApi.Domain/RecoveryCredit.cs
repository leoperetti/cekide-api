namespace CekideApi.Domain;

public enum RecoveryCreditStatus
{
    Available,
    Reserved,
    Used,
    Expired
}

public sealed class RecoveryCredit : Entity
{
    private RecoveryCredit()
    {
    }

    public int RecoveryWeekId { get; private set; }

    public int StudentId { get; private set; }

    public int Ordinal { get; private set; }

    public string OriginPeriod { get; private set; } = string.Empty;

    public int AttendancePeriodId { get; private set; }

    public int PlanLimitsId { get; private set; }

    public DateOnly? NormalStart { get; private set; }

    public DateOnly? NormalEndExclusive { get; private set; }

    public DateOnly? ExceptionStart { get; private set; }

    public DateOnly? ExceptionEndExclusive { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public RecoveryCreditStatus Status { get; private set; }

    public DateTime? UsedAt { get; private set; }

    public static RecoveryCredit Create(RecoveryWeek week, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(week);
        if (week.Id <= 0 || week.GrantedCount is not > 0 || ordinal < 1 || ordinal > week.GrantedCount)
        {
            throw new DomainValidationException("A recovery credit requires a closed week and a valid ordinal.");
        }

        var windows = RecoveryWindowPolicy.GetWindows(week.WeekStart);
        if (windows.Count == 0)
        {
            throw new DomainValidationException("A recovery credit must have at least one valid recovery window.");
        }

        var normal = windows.SingleOrDefault(window => window.Kind == RecoveryWindowKind.Normal);
        var exception = windows.SingleOrDefault(window => window.Kind == RecoveryWindowKind.Exception);
        var expiresAt = GymCalendar.StartOfDayUtc(windows.Max(window => window.EndExclusive));
        return new RecoveryCredit
        {
            RecoveryWeekId = week.Id,
            StudentId = week.StudentId,
            Ordinal = ordinal,
            OriginPeriod = week.OriginPeriod,
            AttendancePeriodId = week.AttendancePeriodId,
            PlanLimitsId = week.PlanLimitsId,
            NormalStart = normal?.Start,
            NormalEndExclusive = normal?.EndExclusive,
            ExceptionStart = exception?.Start,
            ExceptionEndExclusive = exception?.EndExclusive,
            ExpiresAt = expiresAt.UtcDateTime,
            Status = RecoveryCreditStatus.Available
        };
    }

    public bool IsAllowedDate(DateOnly date)
    {
        var inNormal = NormalStart is { } normalStart && NormalEndExclusive is { } normalEnd &&
            date >= normalStart && date < normalEnd;
        var inException = ExceptionStart is { } exceptionStart && ExceptionEndExclusive is { } exceptionEnd &&
            date >= exceptionStart && date < exceptionEnd;
        return GymCalendar.IsRecoveryWeekday(date) && (inNormal || inException);
    }

    public void Reserve(DateOnly date, DateTimeOffset now)
    {
        if (Status != RecoveryCreditStatus.Available || now == default || !IsAllowedDate(date) ||
            now.ToUniversalTime() >= ExpiresAt)
        {
            throw new ConflictException("Recovery credit is not available for the requested date.");
        }

        Status = RecoveryCreditStatus.Reserved;
    }

    public void Release(DateOnly today)
    {
        if (Status != RecoveryCreditStatus.Reserved)
        {
            return;
        }

        Status = HasWindowRemaining(today)
            ? RecoveryCreditStatus.Available
            : RecoveryCreditStatus.Expired;
    }

    public bool HasWindowRemaining(DateOnly today) =>
        NormalStart is { } normalStart && NormalEndExclusive is { } normalEnd &&
        Max(normalStart, today) < normalEnd ||
        ExceptionStart is { } exceptionStart && ExceptionEndExclusive is { } exceptionEnd &&
        Max(exceptionStart, today) < exceptionEnd;

    public void Use(DateTimeOffset now)
    {
        if (Status != RecoveryCreditStatus.Reserved || now == default)
        {
            throw new ConflictException("Only a reserved recovery credit can be used.");
        }

        Status = RecoveryCreditStatus.Used;
        UsedAt = now.UtcDateTime;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status is RecoveryCreditStatus.Available or RecoveryCreditStatus.Reserved &&
            now.ToUniversalTime().UtcDateTime >= ExpiresAt)
        {
            Status = RecoveryCreditStatus.Expired;
        }
    }

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;
}
