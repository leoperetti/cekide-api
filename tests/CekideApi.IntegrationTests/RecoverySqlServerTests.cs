using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CekideApi.Application.Abstractions;
using CekideApi.Application.Notifications;
using CekideApi.Domain;
using CekideApi.Infrastructure.Jobs;
using CekideApi.Infrastructure.Persistence;
using CekideApi.Infrastructure.Storage;
using CekideApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CekideApi.IntegrationTests;

public sealed class RecoverySqlServerTests : IAsyncLifetime
{
    private readonly SqlServerApiFactory _factory;
    private readonly HttpClient _owner;
    private readonly HttpClient _teacher;

    public RecoverySqlServerTests()
    {
        _factory = new SqlServerApiFactory();
        _owner = _factory.CreateClientFor("owner", "owner-1");
        _teacher = _factory.CreateClientFor("teacher", "teacher-1");
    }

    [Fact]
    public async Task A_reservation_and_a_habitual_occupancy_contend_for_the_last_planned_place()
    {
        _factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var (student, creditId) = await CreateStudentWithCreditAsync("last-place");
        var shiftId = await CreateShiftAsync("2026-11-10");
        for (var index = 0; index < 11; index++)
        {
            var other = await CreateStudentAsync($"planned-{index}");
            Assert.Equal(HttpStatusCode.Created, (await _teacher.PostAsJsonAsync(
                $"/api/gym/shifts/{shiftId}/habitual-occupancies",
                new { student_id = other.Id })).StatusCode);
        }

        using var studentClient = _factory.CreateClientFor("user", student.Subject);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reserve = Task.Run(async () =>
        {
            await start.Task;
            return await studentClient.PostAsJsonAsync(
                "/api/gym/me/recovery-reservations",
                new { credit_id = creditId, shift_id = shiftId });
        });
        var habitual = Task.Run(async () =>
        {
            await start.Task;
            return await _teacher.PostAsJsonAsync(
                $"/api/gym/shifts/{shiftId}/habitual-occupancies",
                new { student_id = student.Id });
        });
        start.SetResult();

        var results = await Task.WhenAll(reserve, habitual);
        Assert.Single(results, response => response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.All(results, response => response.Dispose());
    }

    [Fact]
    public async Task Two_connections_cannot_reserve_the_same_credit_for_different_shifts()
    {
        _factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var (student, creditId) = await CreateStudentWithCreditAsync("one-credit");
        var firstShift = await CreateShiftAsync("2026-11-10");
        var secondShift = await CreateShiftAsync("2026-11-11");
        using var firstClient = _factory.CreateClientFor("user", student.Subject);
        using var secondClient = _factory.CreateClientFor("user", student.Subject);
        using var ready = new Barrier(2);
        var first = Task.Run(async () =>
        {
            ready.SignalAndWait(TimeSpan.FromSeconds(5));
            return await firstClient.PostAsJsonAsync(
                "/api/gym/me/recovery-reservations",
                new { credit_id = creditId, shift_id = firstShift });
        });
        var second = Task.Run(async () =>
        {
            ready.SignalAndWait(TimeSpan.FromSeconds(5));
            return await secondClient.PostAsJsonAsync(
                "/api/gym/me/recovery-reservations",
                new { credit_id = creditId, shift_id = secondShift });
        });
        var responses = await Task.WhenAll(first, second);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.All(responses, response => response.Dispose());
    }

    [Fact]
    public async Task Cancelling_a_shift_releases_its_credit_without_waiting_for_a_worker()
    {
        _factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var (student, creditId) = await CreateStudentWithCreditAsync("cancel-shift");
        var shiftId = await CreateShiftAsync("2026-11-10");
        using var studentClient = _factory.CreateClientFor("user", student.Subject);
        var created = await studentClient.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = shiftId });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var cancelled = await _teacher.PostAsync($"/api/gym/shifts/{shiftId}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var history = await studentClient.GetAsync("/api/gym/me/recovery-reservations?period=2026-11");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var reservations = await ReadAsync(history);
        Assert.Equal("cancelled", reservations[0].GetProperty("status").GetString());
        Assert.Equal("shift_cancelled", reservations[0].GetProperty("cancelled_reason").GetString());
        var credits = await studentClient.GetAsync("/api/gym/me/recovery-credits");
        Assert.Equal(HttpStatusCode.OK, credits.StatusCode);
        Assert.Equal("available", (await ReadAsync(credits)).GetProperty("credits")[0]
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task Check_in_stays_ordinary_before_or_after_reservation_and_for_a_cancelled_or_full_shift()
    {
        _factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var (reserved, creditId) = await CreateStudentWithCreditAsync("ordinary-next-to-credit");
        var shiftId = await CreateShiftAsync("2026-11-10");
        using var reservedClient = _factory.CreateClientFor("user", reserved.Subject);
        var reservation = await reservedClient.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = creditId, shift_id = shiftId });
        Assert.Equal(HttpStatusCode.Created, reservation.StatusCode);

        _factory.Clock.Set(new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));
        var beforeCheckIn = await reservedClient.PostAsJsonAsync(
            "/api/gym/me/attendance/check-in", new { dni = reserved.Dni });
        Assert.Equal(HttpStatusCode.Created, beforeCheckIn.StatusCode);
        Assert.Equal("ordinary", (await ReadAsync(beforeCheckIn)).GetProperty("attendance")
            .GetProperty("charge_kind").GetString());
        var reservedCredits = await reservedClient.GetAsync("/api/gym/me/recovery-credits");
        Assert.Equal(HttpStatusCode.OK, reservedCredits.StatusCode);
        Assert.Equal("reserved", (await ReadAsync(reservedCredits)).GetProperty("credits")[0]
            .GetProperty("status").GetString());

        var cancelledShift = await CreateShiftAsync("2026-11-11");
        Assert.Equal(HttpStatusCode.OK,
            (await _teacher.PostAsync($"/api/gym/shifts/{cancelledShift}/cancel", null)).StatusCode);
        var after = await CreateStudentAsync("ordinary-after");
        using var afterClient = _factory.CreateClientFor("user", after.Subject);
        _factory.Clock.Set(new DateTimeOffset(2026, 11, 11, 21, 30, 0, TimeSpan.Zero));
        var afterCheckIn = await afterClient.PostAsJsonAsync(
            "/api/gym/me/attendance/check-in", new { dni = after.Dni });
        Assert.Equal(HttpStatusCode.Created, afterCheckIn.StatusCode);
        Assert.Equal("ordinary", (await ReadAsync(afterCheckIn)).GetProperty("attendance")
            .GetProperty("charge_kind").GetString());

        _factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var kioskAttendee = await CreateStudentWithCreditAsync("full-kiosk-attendee");
        using var kioskAttendeeClient = _factory.CreateClientFor("user", kioskAttendee.Student.Subject);
        var futureReservedShift = await CreateShiftAsync("2026-11-13");
        Assert.Equal(HttpStatusCode.Created, (await kioskAttendeeClient.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = kioskAttendee.CreditId, shift_id = futureReservedShift })).StatusCode);

        var fullShift = await CreateShiftAsync("2026-11-12");
        for (var index = 0; index < 12; index++)
        {
            var habitual = await CreateStudentAsync($"full-{index}");
            Assert.Equal(HttpStatusCode.Created, (await _teacher.PostAsJsonAsync(
                $"/api/gym/shifts/{fullShift}/habitual-occupancies",
                new { student_id = habitual.Id })).StatusCode);
        }

        _factory.Clock.Set(new DateTimeOffset(2026, 11, 12, 20, 30, 0, TimeSpan.Zero));
        using var kiosk = await CreateKioskClientAsync();
        _factory.Clock.Set(new DateTimeOffset(2026, 11, 12, 21, 30, 0, TimeSpan.Zero));
        var fullCheckIn = await kiosk.PostAsJsonAsync(
            "/api/gym/kiosk/attendance/check-in", new { dni = kioskAttendee.Student.Dni });
        Assert.Equal(HttpStatusCode.Created, fullCheckIn.StatusCode);
        var fullAttendance = (await ReadAsync(fullCheckIn)).GetProperty("attendance");
        Assert.Equal("ordinary", fullAttendance.GetProperty("charge_kind").GetString());
        Assert.Equal(fullShift, fullAttendance.GetProperty("shift_id").GetInt32());
        Assert.Equal("kiosk", fullAttendance.GetProperty("source").GetString());
        var kioskCredits = await kioskAttendeeClient.GetAsync("/api/gym/me/recovery-credits");
        Assert.Equal(HttpStatusCode.OK, kioskCredits.StatusCode);
        Assert.Equal("reserved", (await ReadAsync(kioskCredits)).GetProperty("credits")[0]
            .GetProperty("status").GetString());

    }

    [Fact]
    public async Task Self_and_kiosk_check_ins_consume_a_reserved_credit_once_without_quota_outbox()
    {
        _factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var selfStudent = await CreateStudentWithCreditAsync("recovery-self");
        var kioskStudent = await CreateStudentWithCreditAsync("recovery-kiosk");
        var shift = await CreateShiftAsync("2026-11-10");
        using var selfClient = _factory.CreateClientFor("user", selfStudent.Student.Subject);
        using var kioskStudentClient = _factory.CreateClientFor("user", kioskStudent.Student.Subject);

        Assert.Equal(HttpStatusCode.Created, (await selfClient.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = selfStudent.CreditId, shift_id = shift })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await kioskStudentClient.PostAsJsonAsync(
            "/api/gym/me/recovery-reservations",
            new { credit_id = kioskStudent.CreditId, shift_id = shift })).StatusCode);

        _factory.Clock.Set(new DateTimeOffset(2026, 11, 10, 20, 30, 0, TimeSpan.Zero));
        using var kiosk = await CreateKioskClientAsync();
        _factory.Clock.Set(new DateTimeOffset(2026, 11, 10, 21, 30, 0, TimeSpan.Zero));
        var selfFirst = await selfClient.PostAsJsonAsync(
            "/api/gym/me/attendance/check-in", new { dni = selfStudent.Student.Dni });
        var kioskFirst = await kiosk.PostAsJsonAsync(
            "/api/gym/kiosk/attendance/check-in", new { dni = kioskStudent.Student.Dni });
        Assert.Equal(HttpStatusCode.Created, selfFirst.StatusCode);
        Assert.Equal(HttpStatusCode.Created, kioskFirst.StatusCode);
        var selfAttendance = (await ReadAsync(selfFirst)).GetProperty("attendance");
        var kioskAttendance = (await ReadAsync(kioskFirst)).GetProperty("attendance");
        Assert.Equal("recovery", selfAttendance.GetProperty("charge_kind").GetString());
        Assert.Equal("recovery", kioskAttendance.GetProperty("charge_kind").GetString());
        Assert.Equal("self", selfAttendance.GetProperty("source").GetString());
        Assert.Equal("kiosk", kioskAttendance.GetProperty("source").GetString());

        var selfRetry = await selfClient.PostAsJsonAsync(
            "/api/gym/me/attendance/check-in", new { dni = selfStudent.Student.Dni });
        var kioskRetry = await kiosk.PostAsJsonAsync(
            "/api/gym/kiosk/attendance/check-in", new { dni = kioskStudent.Student.Dni });
        Assert.Equal(HttpStatusCode.OK, selfRetry.StatusCode);
        Assert.Equal(HttpStatusCode.OK, kioskRetry.StatusCode);
        var selfRetriedAttendance = (await ReadAsync(selfRetry)).GetProperty("attendance");
        var kioskRetriedAttendance = (await ReadAsync(kioskRetry)).GetProperty("attendance");
        Assert.Equal(selfAttendance.GetProperty("id").GetInt32(),
            selfRetriedAttendance.GetProperty("id").GetInt32());
        Assert.Equal(kioskAttendance.GetProperty("id").GetInt32(),
            kioskRetriedAttendance.GetProperty("id").GetInt32());
        Assert.Equal("recovery", selfRetriedAttendance.GetProperty("charge_kind").GetString());
        Assert.Equal("recovery", kioskRetriedAttendance.GetProperty("charge_kind").GetString());
        Assert.Empty(_factory.Emails.Sent);
        var notices = await _factory.WithDbAsync(db => db.AttendanceQuotaNotifications
            .Where(notification =>
                notification.StudentId == selfStudent.Student.Id ||
                notification.StudentId == kioskStudent.Student.Id)
            .Select(notification => new { notification.StudentId, notification.Period, notification.MonthlyLimit })
            .ToArrayAsync());
        Assert.Empty(notices);
    }

    [Fact]
    public async Task Concurrent_week_closure_and_credit_reads_are_idempotent()
    {
        var (student, _) = await CreateStudentWithCreditAsync("concurrent-close");
        using var firstClient = _factory.CreateClientFor("user", student.Subject);
        using var secondClient = _factory.CreateClientFor("user", student.Subject);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Task.Run(async () =>
        {
            await start.Task;
            return await firstClient.GetAsync("/api/gym/me/recovery-credits");
        });
        var second = Task.Run(async () =>
        {
            await start.Task;
            return await secondClient.GetAsync("/api/gym/me/recovery-credits");
        });
        start.SetResult();

        var responses = await Task.WhenAll(first, second);
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var credits = await Task.WhenAll(responses.Select(ReadAsync));
        var idsByResponse = credits.Select(body => body.GetProperty("credits").EnumerateArray()
                .Select(credit => credit.GetProperty("id").GetInt32())
                .ToArray())
            .ToArray();
        Assert.All(idsByResponse, ids => Assert.Single(ids));
        Assert.Equal(idsByResponse[0][0], idsByResponse[1][0]);
        Assert.All(responses, response => response.Dispose());
    }

    private async Task<RecoveryStudent> CreateStudentAsync(string suffix)
    {
        var subject = $"student-{suffix}-{Guid.NewGuid():N}";
        var dni = Random.Shared.Next(10_000_000, 99_999_999).ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var planResponse = await _owner.PostAsJsonAsync("/api/gym/plans", new
        {
            name = $"Plan {Guid.NewGuid():N}",
            monthly_price = 100m,
            weekly_days = 3,
            monthly_days = 12
        });
        Assert.Equal(HttpStatusCode.Created, planResponse.StatusCode);
        var planId = (await ReadAsync(planResponse)).GetProperty("id").GetInt32();
        var response = await _teacher.PostAsJsonAsync("/api/gym/students", new
        {
            full_name = "Alumno de prueba",
            email = $"{subject}@example.com",
            dni,
            plan_id = planId
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new RecoveryStudent((await ReadAsync(response)).GetProperty("id").GetInt32(), subject, dni);
    }

    private async Task<(RecoveryStudent Student, int CreditId)> CreateStudentWithCreditAsync(string suffix)
    {
        _factory.Clock.SetBuenosAiresDate(2026, 11, 2);
        var student = await CreateStudentAsync(suffix);
        using var client = _factory.CreateClientFor("user", student.Subject);
        foreach (var day in new[] { 2, 3 })
        {
            _factory.Clock.SetBuenosAiresDate(2026, 11, day);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
                "/api/gym/me/attendance/check-in", new { dni = student.Dni })).StatusCode);
        }

        _factory.Clock.SetBuenosAiresDate(2026, 11, 9);
        var response = await client.GetAsync("/api/gym/me/recovery-credits");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var credit = (await ReadAsync(response)).GetProperty("credits").EnumerateArray().Single();
        Assert.Equal("available", credit.GetProperty("status").GetString());
        return (student, credit.GetProperty("id").GetInt32());
    }

    private async Task<int> CreateShiftAsync(string date)
    {
        var response = await _teacher.PostAsJsonAsync("/api/gym/shifts", new
        {
            local_date = date,
            starts_at = "18:00"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("id").GetInt32();
    }

    private static Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>();

    public Task InitializeAsync()
    {
        using var client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    private sealed record RecoveryStudent(int Id, string Subject, string Dni);

    public async Task DisposeAsync()
    {
        _owner.Dispose();
        _teacher.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<HttpClient> CreateKioskClientAsync()
    {
        var session = await _owner.PostAsJsonAsync("/api/gym/kiosk-sessions", new { label = "Recovery test" });
        Assert.Equal(HttpStatusCode.Created, session.StatusCode);
        using var anonymous = _factory.CreateClient();
        var activation = await anonymous.PostAsJsonAsync(
            "/api/gym/kiosk/activate",
            new { activation_code = (await ReadAsync(session)).GetProperty("activation_code").GetString() });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        var token = (await ReadAsync(activation)).GetProperty("token").GetString();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Kiosk", token);
        return client;
    }

    private sealed class SqlServerApiFactory : WebApplicationFactory<Program>, IAsyncDisposable
    {
        private readonly string _connectionString;
        private bool _ownsDatabase;

        public SqlServerApiFactory()
        {
            var parsed = new SqlConnectionStringBuilder(AttendanceSqlServerFixture.ValidateConnectionString(
                Environment.GetEnvironmentVariable(AttendanceSqlServerFixture.ConnectionVariable)))
            {
                InitialCatalog = $"cekide_attendance_test_recovery_{Guid.NewGuid():N}"
            };
            _connectionString = parsed.ConnectionString;
        }

        public FakeClock Clock { get; } = new();

        public FakeEmailSender Emails { get; } = new();

        public FakeAuthcraftInvitationClient Authcraft { get; } = new();

        public async Task<T> WithDbAsync<T>(Func<CekideDbContext, Task<T>> action)
        {
            var options = new DbContextOptionsBuilder<CekideDbContext>()
                .UseSqlServer(_connectionString)
                .Options;
            await using var db = new CekideDbContext(options, Clock);
            return await action(db);
        }

        public HttpClient CreateClientFor(string? role, string subject = "user-1")
        {
            var client = CreateClient();
            if (role is not null)
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", TestTokens.Create(role, subject));
            }

            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = TestTokens.Key,
                    ["Jwt:Issuer"] = TestTokens.Issuer,
                    ["Jwt:Audience"] = TestTokens.Audience,
                    ["Tenant:Id"] = TestTokens.Tenant
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CekideDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CekideDbContext>>();
                services.AddDbContext<CekideDbContext>(options => options.UseSqlServer(_connectionString));
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(Clock);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);
                services.RemoveAll<IAuthcraftInvitationClient>();
                services.AddSingleton<IAuthcraftInvitationClient>(Authcraft);
                services.RemoveAll<IRoutineStorage>();
                services.AddSingleton<IRoutineStorage, InMemoryRoutineStorage>();
                foreach (var hostedService in services.Where(descriptor =>
                             descriptor.ServiceType == typeof(IHostedService)).ToArray())
                {
                    services.Remove(hostedService);
                }
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CekideDbContext>();
            var creator = db.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
            if (creator.Exists())
            {
                throw new InvalidOperationException("The uniquely named recovery test database already exists.");
            }

            _ownsDatabase = true;
            db.Database.Migrate();
            return host;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (!_ownsDatabase)
            {
                return;
            }

            var options = new DbContextOptionsBuilder<CekideDbContext>()
                .UseSqlServer(_connectionString)
                .Options;
            await using var db = new CekideDbContext(options, Clock);
            await db.Database.EnsureDeletedAsync();
        }
    }
}
