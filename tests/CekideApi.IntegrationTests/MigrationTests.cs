using CekideApi.Infrastructure.Persistence;
using CekideApi.Domain;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CekideApi.IntegrationTests;

/// <summary>Guards that the SQL Server migrations always describe the current EF model.</summary>
public class MigrationTests
{
    private static CekideDbContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<CekideDbContext>()
            .UseSqlServer("Server=(localdb)\\unused;Database=cekide-db;Trusted_Connection=True")
            .Options, new FakeClock());

    [Fact]
    public void The_database_has_migrations()
    {
        using var context = SqlServerContext();

        var migrations = context.GetService<IMigrationsAssembly>().Migrations;

        Assert.NotEmpty(migrations);
    }

    [Fact]
    public void The_model_snapshot_matches_the_current_model()
    {
        using var context = SqlServerContext();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);

        var snapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(((IMutableModel)snapshot.Model).FinalizeModel());
        var currentModel = context.GetService<IDesignTimeModel>().Model;

        var differences = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshotModel.GetRelationalModel(), currentModel.GetRelationalModel());

        Assert.Empty(differences);
    }

    [Fact]
    public void Attendance_model_enforces_monthly_snapshots_and_one_check_in_per_local_date()
    {
        using var context = SqlServerContext();
        var periods = context.Model.FindEntityType(typeof(AttendancePeriod))!;
        var attendances = context.Model.FindEntityType(typeof(Attendance))!;
        var kiosks = context.Model.FindEntityType(typeof(KioskSession))!;
        var notifications = context.Model.FindEntityType(typeof(AttendanceQuotaNotification))!;

        Assert.Contains(periods.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(AttendancePeriod.StudentId), nameof(AttendancePeriod.Period)]));
        Assert.Contains(attendances.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Attendance.StudentId), nameof(Attendance.LocalDate)]));
        Assert.Contains(kiosks.GetIndexes(), index =>
            index.IsUnique && index.Properties.Single().Name == nameof(KioskSession.TokenHash));
        Assert.Contains(notifications.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(AttendanceQuotaNotification.StudentId), nameof(AttendanceQuotaNotification.Period)]));
    }

    [Fact]
    public void Shift_model_enforces_capacity_audit_and_restrictive_relations()
    {
        using var context = SqlServerContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var shifts = model.FindEntityType(typeof(GymShift))!;
        var occupancies = model.FindEntityType(typeof(ShiftHabitualOccupancy))!;
        var attendances = model.FindEntityType(typeof(Attendance))!;

        Assert.Contains(shifts.GetCheckConstraints(), constraint => constraint.Name == "CK_gym_shifts_capacity");
        Assert.Contains(shifts.GetCheckConstraints(), constraint => constraint.Name == "CK_gym_shifts_duration");
        Assert.Contains(shifts.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(GymShift.LocalDate), nameof(GymShift.StartsAt)]));
        Assert.Contains(occupancies.GetIndexes(), index =>
            index.IsUnique &&
            index.GetFilter() == "status = 'active'" &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ShiftHabitualOccupancy.StudentId), nameof(ShiftHabitualOccupancy.LocalDate)]));
        Assert.Contains(occupancies.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(GymShift) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(attendances.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(GymShift) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict &&
            foreignKey.Properties.Single().Name == nameof(Attendance.ShiftId));
    }
}

public sealed class AttendanceMigrationSqlServerTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261103042245_AddStudentInvitations";
    private readonly CekideDbContext _context;
    private bool _ownsDatabase;

    public AttendanceMigrationSqlServerTests()
    {
        var connection = new SqlConnectionStringBuilder(AttendanceSqlServerFixture.ValidateConnectionString(
            Environment.GetEnvironmentVariable(AttendanceSqlServerFixture.ConnectionVariable)))
        {
            InitialCatalog = $"cekide_attendance_test_upgrade_{Guid.NewGuid():N}"
        };
        _context = new CekideDbContext(
            new DbContextOptionsBuilder<CekideDbContext>().UseSqlServer(connection.ConnectionString).Options,
            new FakeClock());
    }

    public async Task InitializeAsync()
    {
        var creator = _context.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
        if (await creator.ExistsAsync())
        {
            throw new InvalidOperationException("The uniquely named migration test database already exists.");
        }

        _ownsDatabase = true;
        await _context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
    }

    [Fact]
    public async Task Additive_upgrade_preserves_legacy_profiles_payments_and_enforces_new_constraints()
    {
        var today = new DateOnly(2026, 11, 4);
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO plans (name, monthly_price, is_active, created_at, updated_at)
                VALUES ({"Legacy plan"}, {1000m}, {true}, {now}, {now});
                INSERT INTO students (full_name, email, plan_id, monthly_membership_started_on, is_active, created_at, updated_at)
                VALUES ({"Ana María de la Cruz"}, {"legacy-one@example.test"}, 1, {today}, {true}, {now}, {now}),
                       ({"Otro Alumno"}, {"legacy-two@example.test"}, 1, {today}, {true}, {now}, {now});
                INSERT INTO monthly_payments (student_id, period, amount, status, paid_on, registered_by, registered_at, created_at, updated_at)
                VALUES (1, {"2026-11"}, {1000m}, {"paid"}, {today}, {"owner-test"}, {now}, {now}, {now});
                """);

        await _context.Database.MigrateAsync();

        var students = await _context.Students.OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, students.Count);
        Assert.All(students, s =>
        {
            Assert.Null(s.Dni);
            Assert.Null(s.FirstName);
            Assert.Null(s.LastName);
            Assert.Equal(1, s.PlanId);
            Assert.Equal(today, s.MonthlyMembershipStartedOn);
        });
        Assert.Equal("Ana María de la Cruz", students[0].FullName);
        Assert.Empty(await _context.Set<PlanAttendanceLimits>().ToListAsync());
        var payment = await _context.MonthlyPayments.SingleAsync();
        Assert.Equal(1, payment.StudentId);
        Assert.Equal(1000m, payment.Amount);
        Assert.Equal("2026-11", payment.Period);
        Assert.Equal(now, payment.CreatedAt);

        students[0].Update(students[0].FullName, students[0].Email, null, dni: "01.234.567");
        await _context.SaveChangesAsync();
        students[1].Update(students[1].FullName, students[1].Email, null, dni: "01234567");
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        var uniqueError = Assert.IsType<SqlException>(duplicate.InnerException);
        Assert.Contains(uniqueError.Number, new[] { 2601, 2627 });
        var conflict = await Assert.ThrowsAsync<AttendanceException>(() =>
            new UnitOfWork(_context).SaveChangesAsync());
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal("dni_already_exists", conflict.Code);
        _context.ChangeTracker.Clear();

        var firstStudent = await _context.Students.SingleAsync(s => s.Id == 1);
        var secondStudent = await _context.Students.SingleAsync(s => s.Id == 2);
        firstStudent.LinkIdentity("identity-sql");
        await _context.SaveChangesAsync();
        secondStudent.LinkIdentity("identity-sql");
        var duplicateIdentity = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        var identityError = Assert.IsType<SqlException>(duplicateIdentity.InnerException);
        Assert.Contains(identityError.Number, new[] { 2601, 2627 });
        _context.ChangeTracker.Clear();

        var limits = PlanAttendanceLimits.CreateInitial(1, 7, 2, "owner-test", today);
        _context.Add(limits);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var stored = await _context.Set<PlanAttendanceLimits>().SingleAsync();
        Assert.Equal(7, stored.WeeklyDays);
        Assert.Equal(2, stored.MonthlyDays);
        Assert.Equal(today, stored.EffectiveFrom);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedAt.Kind);

        var checkError = await Assert.ThrowsAsync<SqlException>(() =>
            _context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE plan_attendance_limits SET weekly_days = 8 WHERE id = {stored.Id}
                    """));
        Assert.Equal(547, checkError.Number);
        checkError = await Assert.ThrowsAsync<SqlException>(() =>
            _context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE plan_attendance_limits SET monthly_days = 0 WHERE id = {stored.Id}
                    """));
        Assert.Equal(547, checkError.Number);

        _context.Add(PlanAttendanceLimits.CreateInitial(1, 3, 12, "owner-test", today));
        duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        uniqueError = Assert.IsType<SqlException>(duplicate.InnerException);
        Assert.Contains(uniqueError.Number, new[] { 2601, 2627 });
        _context.ChangeTracker.Clear();

        var period = AttendancePeriod.Create(1, 1, stored.Id, "Legacy plan", 3, 12, today);
        _context.Add(period);
        await _context.SaveChangesAsync();
        var kiosk = KioskSession.Create(
            "Front desk", "cekide-app", "teacher-test", $"activation-{Guid.NewGuid():N}", FakeClock.DefaultNow);
        _context.Add(kiosk);
        await _context.SaveChangesAsync();
        var attendance = Attendance.Create(
            1,
            period.Id,
            today,
            new DateTimeOffset(now),
            AttendanceSource.Self,
            null,
            new AttendanceQuotaSnapshot(1, 1, false, false, false));
        _context.Add(attendance);
        var notification = AttendanceQuotaNotification.Create(
            1,
            period.Id,
            period.Period,
            "legacy-one@example.test",
            "Ana María de la Cruz",
            period.PlanName,
            period.MonthlyLimit,
            new DateTimeOffset(now));
        _context.Add(notification);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        Assert.Single(await _context.Attendances.AsNoTracking().ToListAsync());
        Assert.Equal("2026-11", (await _context.AttendanceQuotaNotifications.AsNoTracking().SingleAsync()).Period);

        var studentToFix = await _context.Students.SingleAsync(s => s.Id == 2);
        studentToFix.Update(studentToFix.FullName, "legacy-one@example.test", null, dni: "98765432");
        await Assert.ThrowsAsync<ConflictException>(() => new UnitOfWork(_context).SaveChangesAsync());
    }

    [Fact]
    public async Task Identity_upgrade_fails_without_rewriting_legacy_duplicate_identities()
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO students (full_name, email, identity_id, is_active, created_at, updated_at)
                VALUES ({"Ana Uno"}, {"ana-one@example.test"}, {"shared-identity"}, {true}, {now}, {now}),
                       ({"Ana Dos"}, {"ana-two@example.test"}, {"shared-identity"}, {true}, {now}, {now});
                """);

        var migrationError = await Assert.ThrowsAsync<SqlException>(() => _context.Database.MigrateAsync());

        Assert.Equal(51000, migrationError.Number);
        Assert.Contains("duplicate identity_id values exist", migrationError.Message);
        var preserved = await _context.Database.SqlQueryRaw<string>(
                "SELECT [identity_id] AS [Value] FROM [students] WHERE [identity_id] = 'shared-identity'")
            .ToListAsync();
        Assert.Equal(2, preserved.Count);
    }

    public async Task DisposeAsync()
    {
        if (_ownsDatabase)
        {
            await _context.Database.EnsureDeletedAsync();
        }

        await _context.DisposeAsync();
    }
}
