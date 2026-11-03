using CekideApi.Application.Abstractions;
using CekideApi.Domain;
using CekideApi.Infrastructure.Persistence;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CekideApi.IntegrationTests;

public sealed class CekideDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeClock _clock = new();

    public CekideDbContextTests()
    {
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CekideDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CekideDbContext>().UseSqlite(_connection).Options, _clock);

    private List<string> Columns(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    [Fact]
    public void Tables_and_columns_use_snake_case()
    {
        Assert.Contains("monthly_price", Columns("plans"));
        Assert.Contains("is_active", Columns("plans"));
        Assert.Contains("price", Columns("pass_tariffs"));
        var students = Columns("students");
        foreach (var column in new[]
                 {
                     "id", "full_name", "email", "phone", "plan_id", "is_active", "identity_id",
                     "routine_blob_path", "routine_file_name", "routine_uploaded_at", "routine_uploaded_by",
                     "created_at", "updated_at"
                 })
        {
            Assert.Contains(column, students);
        }
    }

    [Fact]
    public void Every_table_has_created_at_and_updated_at()
    {
        foreach (var table in new[] { "plans", "pass_tariffs", "students" })
        {
            var columns = Columns(table);
            Assert.Contains("created_at", columns);
            Assert.Contains("updated_at", columns);
        }
    }

    [Fact]
    public async Task Saving_sets_created_at_and_updated_at_from_the_clock_and_updates_only_updated_at()
    {
        await using (var context = NewContext())
        {
            context.Plans.Add(Plan.Create("Básico", 1000m));
            await context.SaveChangesAsync();
        }

        var created = _clock.UtcNow.UtcDateTime;
        _clock.Set(_clock.UtcNow.AddDays(3));

        await using (var context = NewContext())
        {
            var plan = await context.Plans.SingleAsync();
            Assert.Equal(created, plan.CreatedAt);
            Assert.Equal(created, plan.UpdatedAt);
            plan.Update("Básico", 1200m);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var plan = await context.Plans.SingleAsync();
            Assert.Equal(created, plan.CreatedAt);
            Assert.Equal(_clock.UtcNow.UtcDateTime, plan.UpdatedAt);
            Assert.Equal(DateTimeKind.Utc, plan.CreatedAt.Kind);
        }
    }

    [Fact]
    public async Task Plan_name_must_be_unique()
    {
        await using var context = NewContext();
        context.Plans.Add(Plan.Create("Básico", 1000m));
        context.Plans.Add(Plan.Create("Básico", 2000m));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Pass_tariff_type_must_be_unique()
    {
        await using var context = NewContext();
        context.PassTariffs.Add(PassTariff.Create(PassType.Day, 100m));
        context.PassTariffs.Add(PassTariff.Create(PassType.Day, 200m));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Student_email_must_be_unique()
    {
        await using var context = NewContext();
        context.Students.Add(Student.Create("Ana", "ana@example.com", null, null, new DateOnly(2026, 11, 5)));
        context.Students.Add(Student.Create("Otra Ana", "ANA@example.com", null, null, new DateOnly(2026, 11, 5)));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Student_plan_must_exist()
    {
        await using var context = NewContext();
        context.Students.Add(Student.Create("Ana", "ana@example.com", null, 999, new DateOnly(2026, 11, 5)));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Student_round_trips_dates_and_pending_plan()
    {
        await using (var context = NewContext())
        {
            var plan = Plan.Create("Básico", 1000m);
            var other = Plan.Create("Premium", 2000m);
            context.Plans.AddRange(plan, other);
            await context.SaveChangesAsync();

            var student = Student.Create("Ana", "ana@example.com", "123", plan.Id, new DateOnly(2026, 11, 5));
            student.ChangePlan(other.Id, new DateOnly(2026, 11, 20));
            context.Students.Add(student);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var student = await context.Students.SingleAsync();
            Assert.Equal(new DateOnly(2026, 11, 5), student.MonthlyMembershipStartedOn);
            Assert.Equal(new DateOnly(2026, 12, 1), student.PendingPlanEffectiveFrom);
            Assert.NotNull(student.PendingPlanId);
        }
    }

    [Fact]
    public void Money_columns_are_decimal_18_2()
    {
        using var context = NewContext();

        var price = context.Model.FindEntityType(typeof(Plan))!.FindProperty(nameof(Plan.MonthlyPrice))!;
        var tariff = context.Model.FindEntityType(typeof(PassTariff))!.FindProperty(nameof(PassTariff.Price))!;

        Assert.Equal((18, 2), (price.GetPrecision(), price.GetScale()));
        Assert.Equal((18, 2), (tariff.GetPrecision(), tariff.GetScale()));
    }

    [Fact]
    public async Task Only_one_paid_monthly_payment_is_allowed_per_student_and_period_but_voided_ones_do_not_count()
    {
        var now = _clock.UtcNow.UtcDateTime;
        var today = new DateOnly(2026, 11, 5);
        await using var context = NewContext();
        var plan = Plan.Create("Básico", 1000m);
        context.Plans.Add(plan);
        var student = Student.Create("Ana", "ana@example.com", null, null, today);
        context.Students.Add(student);
        await context.SaveChangesAsync();
        var period = PaymentPeriod.Parse("2026-11");

        var voided = MonthlyPayment.Create(student.Id, period, 1000m, today, "t", now);
        voided.Void();
        context.MonthlyPayments.Add(voided);
        context.MonthlyPayments.Add(MonthlyPayment.Create(student.Id, period, 1000m, today, "t", now));
        await context.SaveChangesAsync();

        context.MonthlyPayments.Add(MonthlyPayment.Create(student.Id, period, 1000m, today, "t", now));
        IUnitOfWork unitOfWork = new UnitOfWork(context);

        await Assert.ThrowsAsync<ConflictException>(() => unitOfWork.SaveChangesAsync());
    }

    [Fact]
    public void Payment_tables_use_snake_case_columns()
    {
        Assert.Contains("student_id", Columns("monthly_payments"));
        Assert.Contains("period", Columns("monthly_payments"));
        Assert.Contains("paid_on", Columns("monthly_payments"));
        Assert.Contains("registered_by", Columns("monthly_payments"));
        Assert.Contains("registered_at", Columns("monthly_payments"));
        Assert.Contains("valid_from", Columns("short_term_passes"));
        Assert.Contains("valid_until", Columns("short_term_passes"));
        foreach (var column in new[] { "payment_kind", "payment_id", "action", "old_values", "new_values", "performed_by", "performed_at", "created_at", "updated_at" })
        {
            Assert.Contains(column, Columns("payment_history"));
        }
    }

    [Fact]
    public async Task UnitOfWork_translates_unique_violations_into_conflicts()
    {
        await using var context = NewContext();
        context.Plans.Add(Plan.Create("Básico", 1000m));
        context.Plans.Add(Plan.Create("Básico", 2000m));
        IUnitOfWork unitOfWork = new UnitOfWork(context);

        await Assert.ThrowsAsync<ConflictException>(() => unitOfWork.SaveChangesAsync());
    }
}
