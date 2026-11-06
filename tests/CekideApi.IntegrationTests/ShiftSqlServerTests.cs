using CekideApi.Application.Abstractions;
using CekideApi.Application.Shifts;
using CekideApi.Domain;
using CekideApi.Infrastructure.Persistence;
using CekideApi.Infrastructure.Persistence.Repositories;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CekideApi.IntegrationTests;

public sealed class ShiftSqlServerTests : IAsyncLifetime
{
    private readonly FakeClock _clock = new();
    private readonly string _connectionString;
    private CekideDbContext? _migrationContext;
    private bool _ownsDatabase;

    public ShiftSqlServerTests()
    {
        var connection = new SqlConnectionStringBuilder(AttendanceSqlServerFixture.ValidateConnectionString(
            Environment.GetEnvironmentVariable(AttendanceSqlServerFixture.ConnectionVariable)))
        {
            InitialCatalog = $"cekide_attendance_test_shift_{Guid.NewGuid():N}"
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
            throw new InvalidOperationException("The uniquely named shift test database already exists.");
        }

        _ownsDatabase = true;
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task Concurrent_overlapping_shifts_have_one_winner()
    {
        _clock.SetBuenosAiresDate(2026, 11, 2);
        var first = CreateContext();
        var second = CreateContext();
        var serviceA = CreateShiftService(first);
        var serviceB = CreateShiftService(second);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var one = Task.Run(async () =>
        {
            await start.Task;
            return await CaptureAsync(() =>
                serviceA.CreateAsync(new CreateShiftRequest(new DateOnly(2026, 11, 3), new TimeOnly(18, 0)), "teacher-a"));
        });
        var two = Task.Run(async () =>
        {
            await start.Task;
            return await CaptureAsync(() =>
                serviceB.CreateAsync(new CreateShiftRequest(new DateOnly(2026, 11, 3), new TimeOnly(18, 30)), "teacher-b"));
        });
        start.SetResult();

        try
        {
            var results = await Task.WhenAll(one, two);
            Assert.Equal(1, results.Count(result => result.Error is null));
            Assert.Equal(1, results.Count(result => result.Error is AttendanceException
                { Code: "shift_overlap" }));
            await using var verify = CreateContext();
            Assert.Equal(1, await verify.GymShifts.CountAsync());
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    [Fact]
    public async Task Concurrent_habitual_occupancies_cannot_claim_the_same_last_place()
    {
        var shiftId = await CreateShiftAsync(new DateOnly(2026, 11, 3));
        var students = await CreateStudentsAsync(13);
        var first = CreateContext();
        var second = CreateContext();
        var serviceA = CreateShiftService(first);
        var serviceB = CreateShiftService(second);
        for (var index = 0; index < 11; index++)
        {
            await serviceA.AddHabitualAsync(shiftId, students[index], "seed");
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var one = Task.Run(async () =>
        {
            await start.Task;
            return await CaptureAsync(() => serviceA.AddHabitualAsync(shiftId, students[11], "teacher-a"));
        });
        var two = Task.Run(async () =>
        {
            await start.Task;
            return await CaptureAsync(() => serviceB.AddHabitualAsync(shiftId, students[12], "teacher-b"));
        });
        start.SetResult();

        try
        {
            var results = await Task.WhenAll(one, two);
            Assert.Equal(1, results.Count(result => result.Error is null));
            Assert.Equal(1, results.Count(result => result.Error is AttendanceException
                { Code: "shift_full" }));
            await using var verify = CreateContext();
            var occupancy = await CreateShiftService(verify).GetOccupancyAsync(shiftId);
            Assert.Equal(12, occupancy.PlannedCount);
            Assert.Equal(0, occupancy.Available);
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    [Fact]
    public async Task Concurrent_cancellation_and_check_in_association_never_link_to_a_cancelled_shift()
    {
        var shiftId = await CreateShiftAsync(new DateOnly(2026, 11, 3));
        var studentId = (await CreateStudentsAsync(1)).Single();
        var first = CreateContext();
        var second = CreateContext();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var matching = new ShiftRepository(first);
        var transaction = new AttendanceTransaction(first);
        var cancellation = CreateShiftService(second);
        var checkInAssociation = Task.Run(async () =>
        {
            await start.Task;
            return await transaction.ExecuteForStudentAsync(
                studentId,
                token => matching.FindMatchingOpenShiftAsync(
                    new DateOnly(2026, 11, 3), new TimeOnly(18, 30), token));
        });
        var cancel = Task.Run(async () =>
        {
            await start.Task;
            return await cancellation.CancelAsync(shiftId, "teacher-cancel");
        });
        start.SetResult();

        try
        {
            await Task.WhenAll(checkInAssociation, cancel);
            var matchedShift = await checkInAssociation;
            var cancelledShift = await cancel;
            Assert.True(matchedShift is null || matchedShift == shiftId);
            Assert.Equal(GymShiftStatus.Cancelled, cancelledShift.Status);

            await using var verify = CreateContext();
            Assert.Equal(GymShiftStatus.Cancelled,
                (await verify.GymShifts.SingleAsync(shift => shift.Id == shiftId)).Status);
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    private async Task<int> CreateShiftAsync(DateOnly date)
    {
        _clock.SetBuenosAiresDate(2026, 11, 2);
        await using var context = CreateContext();
        return (await CreateShiftService(context).CreateAsync(
            new CreateShiftRequest(date, new TimeOnly(18, 0)), "teacher")).Id;
    }

    private async Task<List<int>> CreateStudentsAsync(int count)
    {
        await using var context = CreateContext();
        var date = new DateOnly(2026, 11, 3);
        var plan = Plan.Create($"Shift {Guid.NewGuid():N}", 100m);
        context.Plans.Add(plan);
        await context.SaveChangesAsync();
        context.PlanAttendanceLimits.Add(PlanAttendanceLimits.CreateInitial(plan.Id, 3, 12, "sql-test", date));
        var result = new List<Student>();
        for (var index = 0; index < count; index++)
        {
            var student = Student.Create(
                $"Student {index}",
                $"shift-{Guid.NewGuid():N}@example.test",
                null,
                plan.Id,
                date,
                Random.Shared.Next(10_000_000, 99_999_999).ToString());
            context.Students.Add(student);
            result.Add(student);
        }

        await context.SaveChangesAsync();
        return result.Select(student => student.Id).ToList();
    }

    private CekideDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CekideDbContext>().UseSqlServer(_connectionString).Options, _clock);

    private ShiftService CreateShiftService(CekideDbContext context) =>
        new(new ShiftRepository(context), _clock);

    private static async Task<(T? Value, Exception? Error)> CaptureAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return (await operation(), null);
        }
        catch (Exception exception)
        {
            return (default, exception);
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
}
