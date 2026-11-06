using CekideApi.Application.Abstractions;
using CekideApi.Application.Notifications;
using CekideApi.Domain;

namespace CekideApi.UnitTests.Support;

public sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);

    public void Set(DateTimeOffset now) => UtcNow = now;

    /// <summary>Noon (Buenos Aires) of the given local date.</summary>
    public void SetBuenosAiresDate(int year, int month, int day) =>
        UtcNow = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.FromHours(-3)).ToUniversalTime();
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default) => await action();
}

internal static class EntityIds
{
    public static void Assign(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
}

public sealed class InMemoryStudentRepository : IStudentRepository
{
    private int _nextId = 1;

    public List<Student> Items { get; } = [];

    public Task<Student?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<Student>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Student>>(Items.Where(s => ids.Contains(s.Id)).ToList());

    public Task<Student?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Email == normalizedEmail));

    public Task<Student?> GetByIdentityIdAsync(string identityId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.IdentityId == identityId));

    public Task<Student?> GetByDniAsync(string normalizedDni, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(s => s.Dni == normalizedDni));

    public Task<IReadOnlyList<Student>> SearchAsync(string? search, bool? active, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Student>>(Items.Where(s => active is null || s.IsActive == active).ToList());

    public Task<bool> EmailExistsAsync(string normalizedEmail, int? exceptStudentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Any(s => s.Email == normalizedEmail && s.Id != exceptStudentId));

    public Task<bool> DniExistsAsync(string normalizedDni, int? exceptStudentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Any(s => s.Dni == normalizedDni && s.Id != exceptStudentId));

    public void Add(Student student)
    {
        EntityIds.Assign(student, _nextId++);
        Items.Add(student);
    }

    /// <summary>Adds an already-persisted student (monthly when planId is given, starting on <paramref name="startedOn"/>).</summary>
    public Student Seed(string name, string email, int? planId, DateOnly startedOn, bool active = true)
    {
        var student = Student.Create(name, email, null, planId, startedOn);
        Add(student);
        if (!active)
        {
            student.Deactivate();
        }

        return student;
    }
}

public sealed class InMemoryPlanRepository : IPlanRepository
{
    public List<Plan> Items { get; } = [];

    public List<PlanAttendanceLimits> AttendanceLimits { get; } = [];

    public Task<IReadOnlyList<PlanAttendanceLimits>> GetAttendanceLimitsAsync(
        IEnumerable<int> planIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PlanAttendanceLimits>>(AttendanceLimits.Where(l => planIds.Contains(l.PlanId)).ToList());

    public void AddAttendanceLimits(PlanAttendanceLimits limits) => AttendanceLimits.Add(limits);

    public Task<Plan?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Plan>>(Items.ToList());

    public Task<IReadOnlyList<Plan>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Plan>>(Items.Where(p => ids.Contains(p.Id)).ToList());

    public Task<bool> NameExistsAsync(string name, int? exceptPlanId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Any(p => p.Name == name && p.Id != exceptPlanId));

    public void Add(Plan plan)
    {
        EntityIds.Assign(plan, Items.Count + 1);
        Items.Add(plan);
    }
}

public sealed class InMemoryPaymentRepository : IPaymentRepository
{
    private int _nextId = 1;

    public List<MonthlyPayment> Monthly { get; } = [];

    public List<ShortTermPass> Passes { get; } = [];

    public Task<MonthlyPayment?> GetMonthlyAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Monthly.FirstOrDefault(p => p.Id == id));

    public Task<ShortTermPass?> GetPassAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Passes.FirstOrDefault(p => p.Id == id));

    public Task<bool> PaidMonthlyExistsAsync(int studentId, string period, CancellationToken cancellationToken = default) =>
        Task.FromResult(Monthly.Any(p => p.StudentId == studentId && p.Period == period && p.Status == PaymentStatus.Paid));

    public Task<IReadOnlyList<MonthlyPayment>> ListMonthlyForStudentAsync(int studentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MonthlyPayment>>(Monthly.Where(p => p.StudentId == studentId).ToList());

    public Task<IReadOnlyList<ShortTermPass>> ListPassesForStudentAsync(int studentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ShortTermPass>>(Passes.Where(p => p.StudentId == studentId).ToList());

    public Task<IReadOnlyList<MonthlyPayment>> ListMonthlyByPeriodAsync(string period, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MonthlyPayment>>(Monthly.Where(p => p.Period == period).ToList());

    public Task<IReadOnlySet<int>> PaidStudentIdsAsync(string period, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<int>>(
            Monthly.Where(p => p.Period == period && p.Status == PaymentStatus.Paid).Select(p => p.StudentId).ToHashSet());

    public Task<IReadOnlyList<PaymentHistory>> ListHistoryAsync(PaymentKind kind, int paymentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PaymentHistory>>([]);

    public void AddMonthly(MonthlyPayment payment)
    {
        EntityIds.Assign(payment, _nextId++);
        Monthly.Add(payment);
    }

    public void AddPass(ShortTermPass pass)
    {
        EntityIds.Assign(pass, _nextId++);
        Passes.Add(pass);
    }

    public void AddHistory(PaymentHistory entry)
    {
    }

    public void SeedPaid(Student student, string period, decimal amount = 15000m) =>
        AddMonthly(MonthlyPayment.Create(
            student.Id, PaymentPeriod.Parse(period), amount, new DateOnly(2026, 11, 2), "teacher-1", DateTime.UtcNow));
}

public sealed class InMemoryOverdueNotificationRepository : IOverdueNotificationRepository
{
    private int _nextId = 1;

    public List<OverdueNotification> Items { get; } = [];

    public Task<OverdueNotification?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(n => n.Id == id));

    public Task<IReadOnlyList<OverdueNotification>> ListByPeriodAsync(string period, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OverdueNotification>>(Items.Where(n => n.Period == period).ToList());

    public Task<IReadOnlyList<OverdueNotification>> ListAsync(
        string? period, NotificationStatus? status, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OverdueNotification>>(
            Items.Where(n => (period is null || n.Period == period) && (status is null || n.Status == status)).ToList());

    /// <summary>Mimics the unique (student_id, period) index.</summary>
    public void Add(OverdueNotification notification)
    {
        if (Items.Any(n => n.StudentId == notification.StudentId && n.Period == notification.Period))
        {
            throw new ConflictException("The record conflicts with an existing one.");
        }

        EntityIds.Assign(notification, _nextId++);
        Items.Add(notification);
    }

    public void Discard(OverdueNotification notification) => Items.Remove(notification);
}

public sealed class InMemoryAttendanceQuotaNotificationRepository : IAttendanceQuotaNotificationRepository
{
    private int _nextId = 1;

    public List<AttendanceQuotaNotification> Items { get; } = [];

    public Task<AttendanceQuotaNotification?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(notification => notification.Id == id));

    public Task<IReadOnlyList<AttendanceQuotaNotification>> ListAsync(
        string? period,
        AttendanceQuotaNotificationStatus? status,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AttendanceQuotaNotification>>(
            Items.Where(notification =>
                    (period is null || notification.Period == period) &&
                    (status is null || notification.Status == status))
                .OrderByDescending(notification => notification.Period, StringComparer.Ordinal)
                .ThenByDescending(notification => notification.Id)
                .ToArray());

    public Task<IReadOnlyList<int>> ListRunnableIdsAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<int>>(Items
            .Where(notification =>
                notification.Status == AttendanceQuotaNotificationStatus.Pending ||
                notification.Status == AttendanceQuotaNotificationStatus.Failed &&
                notification.Attempts <= AttendanceQuotaNotificationPolicy.MaximumAutomaticRetries &&
                notification.NextAttemptAt <= now.UtcDateTime)
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .Take(limit)
            .Select(notification => notification.Id)
            .ToArray());

    public Task<int> MarkExpiredClaimsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var expired = Items
            .Where(notification =>
                notification.Status == AttendanceQuotaNotificationStatus.Sending &&
                notification.ClaimedUntil <= now.UtcDateTime)
            .ToArray();
        foreach (var notification in expired)
        {
            notification.ExpireClaim(now);
        }

        return Task.FromResult(expired.Length);
    }

    public Task<bool> TryClaimAsync(
        int id,
        string claimId,
        DateTimeOffset claimedAt,
        TimeSpan lease,
        CancellationToken cancellationToken = default)
    {
        var notification = Items.FirstOrDefault(item => item.Id == id);
        return Task.FromResult(notification is not null &&
                               (notification.Status != AttendanceQuotaNotificationStatus.Failed ||
                                notification.Attempts <= AttendanceQuotaNotificationPolicy.MaximumAutomaticRetries) &&
                               notification.TryClaim(claimId, claimedAt, lease));
    }

    public Task<bool> MarkSentAsync(
        int id,
        string claimId,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(WithClaim(id, claimId, notification => notification.MarkSent(claimId, sentAt)));

    public Task<bool> MarkFailedAsync(
        int id,
        string claimId,
        string error,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(WithClaim(
            id,
            claimId,
            notification => notification.MarkFailed(claimId, error, retryAt)));

    public Task<bool> MarkDeliveryUnknownAsync(
        int id,
        string claimId,
        string error,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(WithClaim(id, claimId, notification => notification.MarkDeliveryUnknown(claimId, error)));

    public Task<bool> QueueManualRetryAsync(int id, CancellationToken cancellationToken = default)
    {
        var notification = Items.FirstOrDefault(item => item.Id == id);
        if (notification?.Status != AttendanceQuotaNotificationStatus.Failed)
        {
            return Task.FromResult(false);
        }

        notification.QueueManualRetry();
        return Task.FromResult(true);
    }

    public Task<bool> ResolveDeliveryAsync(
        int id,
        bool delivered,
        string resolvedBy,
        string evidence,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken = default)
    {
        var notification = Items.FirstOrDefault(item =>
            item.Id == id && item.Status == AttendanceQuotaNotificationStatus.DeliveryUnknown);
        if (notification is null)
        {
            return Task.FromResult(false);
        }

        notification.ResolveDelivery(resolvedBy, delivered, evidence, resolvedAt);
        return Task.FromResult(true);
    }

    public void Add(AttendanceQuotaNotification notification)
    {
        EntityIds.Assign(notification, _nextId++);
        notification.CreatedAt = DateTime.UtcNow;
        notification.UpdatedAt = notification.CreatedAt;
        Items.Add(notification);
    }

    private bool WithClaim(int id, string claimId, Action<AttendanceQuotaNotification> update)
    {
        var notification = Items.FirstOrDefault(item => item.Id == id);
        if (notification?.Status != AttendanceQuotaNotificationStatus.Sending ||
            !string.Equals(notification.ClaimId, claimId, StringComparison.Ordinal))
        {
            return false;
        }

        update(notification);
        return true;
    }
}

public sealed class InMemoryStudentInvitationRepository : IStudentInvitationRepository
{
    private int _nextId = 1;

    public List<StudentInvitation> Items { get; } = [];

    public Task<IReadOnlyList<StudentInvitation>> ListForStudentAsync(int studentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StudentInvitation>>(Items.Where(i => i.StudentId == studentId).OrderBy(i => i.Id).ToList());

    public Task<StudentInvitation?> GetLatestByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items
            .Where(i => i.Email == normalizedEmail)
            .OrderByDescending(i => i.SentAt)
            .ThenByDescending(i => i.Id)
            .FirstOrDefault());

    public void Add(StudentInvitation invitation)
    {
        EntityIds.Assign(invitation, _nextId++);
        Items.Add(invitation);
    }

    public Task<IReadOnlyDictionary<int, InvitationStatus>> LatestStatusesAsync(
        IEnumerable<int> studentIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<int, InvitationStatus>>(
            Items.Where(i => studentIds.Contains(i.StudentId))
                .GroupBy(i => i.StudentId)
                .ToDictionary(g => g.Key, g => g.OrderBy(i => i.Id).Last().Status));
}

public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public int Attempts { get; private set; }

    public EmailSendResult? Result { get; set; }

    /// <summary>When set, every send fails with this error.</summary>
    public string? FailWith { get; set; }

    /// <summary>When set, every send throws it.</summary>
    public Exception? ThrowWith { get; set; }

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Attempts++;
        if (ThrowWith is not null)
        {
            throw ThrowWith;
        }

        if (Result is not null)
        {
            return Task.FromResult(Result);
        }

        if (FailWith is not null)
        {
            return Task.FromResult(EmailSendResult.Fail(FailWith));
        }

        Sent.Add(message);
        return Task.FromResult(EmailSendResult.Ok());
    }
}

public sealed class NoopAttendanceRepository : IAttendanceRepository
{
    public Task<Attendance?> GetForStudentDateAsync(
        int studentId,
        DateOnly localDate,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<Attendance?>(null);

    public Task<AttendancePeriod?> GetPeriodAsync(
        int studentId,
        string period,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AttendancePeriod?>(null);

    public Task<IReadOnlyList<DateOnly>> GetAttendanceDatesAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DateOnly>>([]);

    public Task<IReadOnlyList<Attendance>> GetAttendancesAsync(
        int studentId,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Attendance>>([]);

    public Task<AttendanceQuotaNotification?> GetQuotaNotificationAsync(
        int studentId,
        string period,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AttendanceQuotaNotification?>(null);

    public void AddPeriod(AttendancePeriod period)
    {
    }

    public void AddAttendance(Attendance attendance)
    {
    }

    public void AddQuotaNotification(AttendanceQuotaNotification notification)
    {
    }
}
