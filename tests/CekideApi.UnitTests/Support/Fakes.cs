using CekideApi.Application.Abstractions;
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

    public Task<IReadOnlyList<Student>> SearchAsync(string? search, bool? active, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Student>>(Items.Where(s => active is null || s.IsActive == active).ToList());

    public Task<bool> EmailExistsAsync(string normalizedEmail, int? exceptStudentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Any(s => s.Email == normalizedEmail && s.Id != exceptStudentId));

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

public sealed class InMemoryStudentInvitationRepository : IStudentInvitationRepository
{
    private int _nextId = 1;

    public List<StudentInvitation> Items { get; } = [];

    public Task<IReadOnlyList<StudentInvitation>> ListForStudentAsync(int studentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StudentInvitation>>(Items.Where(i => i.StudentId == studentId).OrderBy(i => i.Id).ToList());

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

        if (FailWith is not null)
        {
            return Task.FromResult(EmailSendResult.Fail(FailWith));
        }

        Sent.Add(message);
        return Task.FromResult(EmailSendResult.Ok());
    }
}
