using CekideApi.Application.Abstractions;
using CekideApi.Application.Attendance;
using CekideApi.Application.Kiosks;
using CekideApi.Application.Payments;
using CekideApi.Application.Recoveries;
using CekideApi.Application.Students;
using CekideApi.Application.Notifications;
using CekideApi.Authorization;
using CekideApi.Domain;
using CekideApi.Infrastructure.Persistence;
using CekideApi.Infrastructure.Persistence.Repositories;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CekideApi.IntegrationTests;

public sealed class AttendanceSqlServerTests : IAsyncLifetime
{
    private readonly FakeClock _clock = new();
    private readonly string _connectionString;
    private CekideDbContext? _migrationContext;
    private bool _ownsDatabase;

    public AttendanceSqlServerTests()
    {
        var connection = new SqlConnectionStringBuilder(AttendanceSqlServerFixture.ValidateConnectionString(
            Environment.GetEnvironmentVariable(AttendanceSqlServerFixture.ConnectionVariable)))
        {
            InitialCatalog = $"cekide_attendance_test_checkin_{Guid.NewGuid():N}"
        };
        _connectionString = connection.ConnectionString;
        _migrationContext = new CekideDbContext(
            new DbContextOptionsBuilder<CekideDbContext>().UseSqlServer(_connectionString).Options,
            _clock);
    }

    public async Task InitializeAsync()
    {
        var context = _migrationContext
            ?? throw new InvalidOperationException("The SQL Server test context was not initialized.");
        var creator = context.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        if (await creator.ExistsAsync())
        {
            throw new InvalidOperationException("The uniquely named check-in test database already exists.");
        }

        _ownsDatabase = true;
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task Concurrent_self_and_kiosk_requests_create_one_attendance_and_one_quota_notice()
    {
        var data = await CreateStudentAsync(monthlyLimit: 1);
        var firstContext = CreateContext();
        var secondContext = CreateContext();
        try
        {
            var selfTask = CreateCheckInService(firstContext).CheckInSelfAsync(data.StudentId, data.Dni);
            var kioskTask = CreateCheckInService(secondContext).CheckInKioskByDniAsync(
                data.Dni, data.KioskSessionId, TenantRequirementHandler.DefaultTenantId);
            var results = await Task.WhenAll(selfTask, kioskTask);

            Assert.Contains(results, result => result.Outcome == AttendanceCheckInOutcome.Created);
            Assert.Contains(results, result => result.Outcome == AttendanceCheckInOutcome.AlreadyCheckedIn);

            await using var verification = CreateContext();
            Assert.Equal(1, await verification.Attendances.CountAsync(a => a.StudentId == data.StudentId));
            Assert.Equal(1, await verification.AttendancePeriods.CountAsync(p => p.StudentId == data.StudentId));
            Assert.Equal(1, await verification.AttendanceQuotaNotifications.CountAsync(
                notification => notification.StudentId == data.StudentId));

            _clock.SetBuenosAiresDate(2026, 11, 6);
            var nextDay = await CreateCheckInService(verification).CheckInSelfAsync(data.StudentId, data.Dni);
            Assert.Equal(AttendanceCheckInOutcome.Created, nextDay.Outcome);
            Assert.True(nextDay.Attendance.MonthlyExceeded);
            Assert.Equal(2, nextDay.Summary.Month.Consumed);
            Assert.Equal(1, await verification.AttendanceQuotaNotifications.CountAsync(
                notification => notification.StudentId == data.StudentId));
        }
        finally
        {
            await firstContext.DisposeAsync();
            await secondContext.DisposeAsync();
        }
    }

    [Fact]
    public async Task Student_dni_updates_wait_for_check_in_lock_and_are_used_by_later_requests()
    {
        var data = await CreateStudentAsync(monthlyLimit: 12);
        await using var lockContext = CreateContext();
        await using var updateContext = CreateContext();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lockTask = new AttendanceTransaction(lockContext).ExecuteForStudentAsync(
            data.StudentId,
            async cancellationToken =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
                return true;
            });

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newDni = CreateDni();
        var updateTask = CreateStudentService(updateContext).UpdateAsync(
            data.StudentId,
            new UpdateStudentRequest("Ana Pérez", data.Email, null, null, newDni));

        try
        {
            await Task.Delay(200);
            Assert.False(updateTask.IsCompleted);
        }
        finally
        {
            release.TrySetResult(true);
        }

        await lockTask.WaitAsync(TimeSpan.FromSeconds(10));
        var updated = await updateTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(newDni, updated.Dni);

        await using var checkInContext = CreateContext();
        var service = CreateCheckInService(checkInContext);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CheckInSelfAsync(data.StudentId, data.Dni));
        var result = await service.CheckInSelfAsync(data.StudentId, newDni);
        Assert.Equal(AttendanceCheckInOutcome.Created, result.Outcome);
    }

    [Fact]
    public async Task Concurrent_kiosk_revocation_prevents_all_later_check_ins()
    {
        var data = await CreateStudentAsync(monthlyLimit: 12);
        await using var revokeContext = CreateContext();
        await using var checkInContext = CreateContext();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var revokeTask = Task.Run(async () =>
        {
            await start.Task;
            await CreateKioskService(revokeContext).RevokeAsync(
                data.KioskSessionId,
                TenantRequirementHandler.DefaultTenantId,
                "sql-test");
        });
        var concurrentCheckInTask = Task.Run(async () =>
        {
            await start.Task;
            try
            {
                return await CreateCheckInService(checkInContext).CheckInKioskByDniAsync(
                    data.Dni,
                    data.KioskSessionId,
                    TenantRequirementHandler.DefaultTenantId);
            }
            catch (AttendanceException exception) when (exception.Code == "kiosk_not_authorized")
            {
                return null;
            }
        });

        start.SetResult();
        await Task.WhenAll(revokeTask, concurrentCheckInTask);
        var concurrentResult = await concurrentCheckInTask;
        Assert.True(concurrentResult is null || concurrentResult.Outcome == AttendanceCheckInOutcome.Created);

        await using var verification = CreateContext();
        var kiosk = await verification.KioskSessions.SingleAsync(session => session.Id == data.KioskSessionId);
        Assert.NotNull(kiosk.RevokedAt);

        var attemptsBeforePostRevoke = await verification.Attendances.CountAsync(
            attendance => attendance.StudentId == data.StudentId);
        var exception = await Assert.ThrowsAsync<AttendanceException>(() =>
            CreateCheckInService(verification).CheckInKioskByDniAsync(
                data.Dni,
                data.KioskSessionId,
                TenantRequirementHandler.DefaultTenantId));
        Assert.Equal("kiosk_not_authorized", exception.Code);
        Assert.Equal(
            attemptsBeforePostRevoke,
            await verification.Attendances.CountAsync(attendance => attendance.StudentId == data.StudentId));
    }

    [Fact]
    public async Task Monthly_snapshot_survives_future_plan_limit_revision()
    {
        var data = await CreateStudentAsync(monthlyLimit: 12);
        await using (var novemberContext = CreateContext())
        {
            var november = await CreateCheckInService(novemberContext)
                .CheckInSelfAsync(data.StudentId, data.Dni);
            Assert.Equal(12, november.Summary.Month.Limit);

            var student = await novemberContext.Students.SingleAsync(
                student => student.Id == data.StudentId);
            var plan = student.PlanId;
            Assert.NotNull(plan);
            novemberContext.PlanAttendanceLimits.Add(PlanAttendanceLimits.CreateRevision(
                plan.Value,
                weeklyDays: 4,
                monthlyDays: 16,
                configuredBy: "sql-test",
                today: DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime)));
            await novemberContext.SaveChangesAsync();
        }

        _clock.SetBuenosAiresDate(2026, 11, 6);
        await using (var novemberContext = CreateContext())
        {
            var november = await CreateCheckInService(novemberContext)
                .CheckInSelfAsync(data.StudentId, data.Dni);
            Assert.Equal(12, november.Summary.Month.Limit);
            Assert.Equal(2, november.Summary.Month.Consumed);
        }

        _clock.SetBuenosAiresDate(2026, 12, 1);
        await using (var decemberContext = CreateContext())
        {
            var december = await CreateCheckInService(decemberContext)
                .CheckInSelfAsync(data.StudentId, data.Dni);
            Assert.Equal(16, december.Summary.Month.Limit);
            Assert.Equal(1, december.Summary.Month.Consumed);

            var novemberSnapshot = await decemberContext.AttendancePeriods
                .SingleAsync(period => period.StudentId == data.StudentId && period.Period == "2026-11");
            var decemberSnapshot = await decemberContext.AttendancePeriods
                .SingleAsync(period => period.StudentId == data.StudentId && period.Period == "2026-12");
            Assert.Equal(12, novemberSnapshot.MonthlyLimit);
            Assert.Equal(16, decemberSnapshot.MonthlyLimit);
        }
    }

    public async Task DisposeAsync()
    {
        if (_migrationContext is not null)
        {
            if (_ownsDatabase)
            {
                await _migrationContext.Database.EnsureDeletedAsync();
            }

            await _migrationContext.DisposeAsync();
        }
    }

    private CekideDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CekideDbContext>().UseSqlServer(_connectionString).Options, _clock);

    private async Task<TestStudent> CreateStudentAsync(int monthlyLimit)
    {
        await using var context = CreateContext();
        var today = GymCalendar.Today(_clock.UtcNow);
        var plan = Plan.Create($"Attendance {Guid.NewGuid():N}", 100m);
        context.Plans.Add(plan);
        await context.SaveChangesAsync();

        context.PlanAttendanceLimits.Add(
            PlanAttendanceLimits.CreateInitial(plan.Id, 3, monthlyLimit, "sql-test", today));
        var dni = CreateDni();
        var email = $"student-{Guid.NewGuid():N}@example.test";
        var student = Student.Create("Ana Pérez", email, null, plan.Id, today, dni);
        context.Students.Add(student);

        var activationCode = $"activation-{Guid.NewGuid():N}";
        var kiosk = KioskSession.Create(
            "SQL check-in",
            TenantRequirementHandler.DefaultTenantId,
            "sql-test",
            KioskSessionService.Hash(activationCode),
            _clock.UtcNow);
        kiosk.Activate(
            KioskSessionService.Hash(activationCode),
            KioskSessionService.Hash($"token-{Guid.NewGuid():N}"),
            _clock.UtcNow);
        context.KioskSessions.Add(kiosk);
        await context.SaveChangesAsync();

        return new TestStudent(student.Id, kiosk.Id, email, dni);
    }

    private AttendanceCheckInService CreateCheckInService(CekideDbContext context)
    {
        var plans = new PlanRepository(context);
        var attendances = new AttendanceRepository(context);
        var quotaNotifications = new AttendanceQuotaNotificationService(
            plans,
            attendances,
            new AttendanceQuotaNotificationRepository(context, _clock),
            new FakeEmailSender(),
            _clock);
        return new AttendanceCheckInService(
            new StudentRepository(context),
            plans,
            attendances,
            new KioskSessionRepository(context),
            new AttendanceTransaction(context),
            new UnitOfWork(context),
            _clock,
            quotaNotifications);
    }

    private KioskSessionService CreateKioskService(CekideDbContext context) =>
        new(new KioskSessionRepository(context), new UnitOfWork(context), _clock);

    private StudentService CreateStudentService(CekideDbContext context)
    {
        var students = new StudentRepository(context);
        var plans = new PlanRepository(context);
        var unitOfWork = new UnitOfWork(context);
        var attendanceTransaction = new AttendanceTransaction(context);
        var payments = new PaymentService(
            new PaymentRepository(context),
            students,
            plans,
            new PassTariffRepository(context),
            unitOfWork,
            _clock);
        var recoveryAccrual = new RecoveryAccrualService(
            new RecoveryRepository(context),
            students,
            plans,
            new AttendanceRepository(context),
            attendanceTransaction,
            unitOfWork,
            _clock);
        return new StudentService(
            students,
            plans,
            unitOfWork,
            _clock,
            payments,
            new StudentInvitationRepository(context),
            attendanceTransaction,
            recoveryAccrual);
    }

    private static string CreateDni() =>
        Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed record TestStudent(int StudentId, int KioskSessionId, string Email, string Dni);
}
