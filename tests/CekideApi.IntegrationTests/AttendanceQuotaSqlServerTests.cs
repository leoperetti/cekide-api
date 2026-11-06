using CekideApi.Application.Notifications;
using CekideApi.Domain;
using CekideApi.Infrastructure.Persistence;
using CekideApi.Infrastructure.Persistence.Repositories;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CekideApi.IntegrationTests;

public sealed class AttendanceQuotaSqlServerTests : IAsyncLifetime
{
    private readonly FakeClock _clock = new();
    private readonly string _connectionString;
    private readonly CekideDbContext _migrationContext;
    private bool _ownsDatabase;

    public AttendanceQuotaSqlServerTests()
    {
        var connection = new SqlConnectionStringBuilder(AttendanceSqlServerFixture.ValidateConnectionString(
            Environment.GetEnvironmentVariable(AttendanceSqlServerFixture.ConnectionVariable)))
        {
            InitialCatalog = $"cekide_attendance_test_quota_{Guid.NewGuid():N}"
        };
        _connectionString = connection.ConnectionString;
        _clock.SetBuenosAiresDate(2026, 11, 5);
        _migrationContext = CreateContext();
    }

    public async Task InitializeAsync()
    {
        var creator = _migrationContext.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        if (await creator.ExistsAsync())
        {
            throw new InvalidOperationException("The uniquely named quota test database already exists.");
        }

        _ownsDatabase = true;
        await _migrationContext.Database.MigrateAsync();
    }

    [Fact]
    public async Task Two_dispatchers_cannot_claim_one_notification_and_unknown_delivery_is_never_resent()
    {
        var id = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.Pending);
        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstRepository = new AttendanceQuotaNotificationRepository(first, _clock);
        var secondRepository = new AttendanceQuotaNotificationRepository(second, _clock);
        var claims = await Task.WhenAll(
            firstRepository.TryClaimAsync(id, "worker-one", _clock.UtcNow, AttendanceQuotaNotificationPolicy.ClaimLease),
            secondRepository.TryClaimAsync(id, "worker-two", _clock.UtcNow, AttendanceQuotaNotificationPolicy.ClaimLease));

        Assert.Single(claims, claimed => claimed);
        var claimId = claims[0] ? "worker-one" : "worker-two";
        var ownerRepository = claims[0] ? firstRepository : secondRepository;
        Assert.True(await ownerRepository.MarkDeliveryUnknownAsync(
            id, claimId, AttendanceQuotaNotificationPolicy.DeliveryUnknownError));

        await using var dispatchContext = CreateContext();
        var email = new FakeEmailSender();
        var service = new AttendanceQuotaNotificationService(
            new PlanRepository(dispatchContext),
            new AttendanceRepository(dispatchContext),
            new AttendanceQuotaNotificationRepository(dispatchContext, _clock),
            email,
            _clock);

        var dispatch = await service.ProcessDueAsync();
        var stored = await dispatchContext.AttendanceQuotaNotifications.SingleAsync(notification => notification.Id == id);

        Assert.Equal(0, dispatch.Sent);
        Assert.Equal(0, email.Attempts);
        Assert.Equal(AttendanceQuotaNotificationStatus.DeliveryUnknown, stored.Status);
        Assert.Equal(1, stored.Attempts);
    }

    [Fact]
    public async Task Concurrent_manual_retry_and_dispatch_claims_have_one_winner()
    {
        var id = await CreateNotificationAsync(AttendanceQuotaNotificationStatus.Failed);
        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstRepository = new AttendanceQuotaNotificationRepository(first, _clock);
        var secondRepository = new AttendanceQuotaNotificationRepository(second, _clock);

        var retries = await Task.WhenAll(
            firstRepository.QueueManualRetryAsync(id),
            secondRepository.QueueManualRetryAsync(id));

        Assert.Single(retries, queued => queued);
        var claims = await Task.WhenAll(
            firstRepository.TryClaimAsync(id, "worker-one", _clock.UtcNow, AttendanceQuotaNotificationPolicy.ClaimLease),
            secondRepository.TryClaimAsync(id, "worker-two", _clock.UtcNow, AttendanceQuotaNotificationPolicy.ClaimLease));

        Assert.Single(claims, claimed => claimed);
    }

    public async Task DisposeAsync()
    {
        if (_ownsDatabase)
        {
            await _migrationContext.Database.EnsureDeletedAsync();
        }

        await _migrationContext.DisposeAsync();
    }

    private async Task<int> CreateNotificationAsync(AttendanceQuotaNotificationStatus initialStatus)
    {
        await using var context = CreateContext();
        var today = new DateOnly(2026, 11, 5);
        var plan = Plan.Create($"Quota SQL {Guid.NewGuid():N}", 100m);
        context.Plans.Add(plan);
        await context.SaveChangesAsync();

        var limits = PlanAttendanceLimits.CreateInitial(plan.Id, 3, 12, "sql-test", today);
        context.PlanAttendanceLimits.Add(limits);
        await context.SaveChangesAsync();

        var email = $"quota-{Guid.NewGuid():N}@example.test";
        var student = Student.Create("Ana Pérez", email, null, plan.Id, today, "12345678");
        context.Students.Add(student);
        await context.SaveChangesAsync();

        var period = AttendancePeriod.Create(student.Id, plan.Id, limits.Id, plan.Name, 3, 12, today);
        context.AttendancePeriods.Add(period);
        await context.SaveChangesAsync();

        var notification = AttendanceQuotaNotification.Create(
            student.Id, period.Id, period.Period, email, student.FullName, period.PlanName, 12, _clock.UtcNow);
        if (initialStatus == AttendanceQuotaNotificationStatus.Failed)
        {
            Assert.True(notification.TryClaim(
                "seed-claim", _clock.UtcNow, AttendanceQuotaNotificationPolicy.ClaimLease));
            notification.MarkFailed("seed-claim", "Provider rejected", _clock.UtcNow.AddMinutes(-1));
        }

        context.AttendanceQuotaNotifications.Add(notification);
        await context.SaveChangesAsync();
        return notification.Id;
    }

    private CekideDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CekideDbContext>().UseSqlServer(_connectionString).Options, _clock);
}
