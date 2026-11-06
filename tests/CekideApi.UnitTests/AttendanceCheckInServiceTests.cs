using CekideApi.Application.Abstractions;
using CekideApi.Application.Attendance;
using CekideApi.Application.Notifications;
using CekideApi.Application.Shifts;
using CekideApi.Domain;
using CekideApi.UnitTests.Support;

namespace CekideApi.UnitTests;

public class AttendanceCheckInServiceTests
{
    [Fact]
    public void Quota_notice_requires_an_ordinary_transition_from_below_the_limit()
    {
        Assert.True(AttendanceQuotaNotificationService.ShouldCreateForCheckIn(
            AttendanceChargeKind.Ordinary, new RecoveryMonthUsage(11, 0, 0, 0), 12, 12));
        Assert.False(AttendanceQuotaNotificationService.ShouldCreateForCheckIn(
            AttendanceChargeKind.Recovery, new RecoveryMonthUsage(11, 0, 0, 0), 12, 12));
        Assert.False(AttendanceQuotaNotificationService.ShouldCreateForCheckIn(
            AttendanceChargeKind.Ordinary, new RecoveryMonthUsage(12, 0, 0, 0), 13, 12));
    }

    [Fact]
    public void Held_credit_can_exhaust_available_days_without_triggering_a_quota_notice()
    {
        var before = new RecoveryMonthUsage(10, 0, 0, 1);
        var after = RecoveryAccounting.SummarizeMonth(12, 11, 0, 0, 1);

        Assert.Equal(11, after.Consumed);
        Assert.Equal(0, after.Remaining);
        Assert.False(AttendanceQuotaNotificationService.ShouldCreateForCheckIn(
            AttendanceChargeKind.Ordinary, before, after.Consumed, monthlyLimit: 12));
    }

    [Fact]
    public async Task Check_in_does_not_read_shift_occupancy_to_decide_quota_notifications()
    {
        var shifts = new FullShiftRepository();
        var fixture = new Fixture(
            DateTimeOffset.Parse("2026-11-05T15:00:00Z"),
            weeklyLimit: 3,
            monthlyLimit: 12,
            shifts: shifts);

        var result = await fixture.Service.CheckInSelfAsync(fixture.Student.Id, "12345678");

        Assert.Equal(AttendanceCheckInOutcome.Created, result.Outcome);
        Assert.Empty(fixture.AttendanceRepository.Notifications);
        Assert.Equal(0, shifts.OccupancyReads);
    }

    [Fact]
    public async Task Extra_weekly_attendance_is_recorded_and_monthly_quota_notice_is_created_only_at_the_limit()
    {
        var fixture = new Fixture(new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero), weeklyLimit: 3, monthlyLimit: 4);
        fixture.SeedAttendance(new DateOnly(2026, 11, 2));
        fixture.SeedAttendance(new DateOnly(2026, 11, 3));
        fixture.SeedAttendance(new DateOnly(2026, 11, 4));

        var result = await fixture.Service.CheckInSelfAsync(fixture.Student.Id, "12.345.678");

        Assert.Equal(AttendanceCheckInOutcome.Created, result.Outcome);
        Assert.Equal(4, result.Summary.Week.Consumed);
        Assert.Equal(4, result.Summary.Month.Consumed);
        Assert.Equal(0, result.Summary.Month.Remaining);
        Assert.Equal(["weekly_limit_exceeded", "monthly_quota_exhausted"], result.Warnings);
        Assert.True(fixture.AttendanceRepository.Items.Count == 4);
        Assert.Single(fixture.AttendanceRepository.Notifications);
        Assert.Equal(AttendanceQuotaNotificationStatus.Pending, result.QuotaNoticeStatus);
    }

    [Fact]
    public async Task Quota_notice_recommends_the_lowest_higher_active_monthly_limit()
    {
        var fixture = new Fixture(
            new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero),
            weeklyLimit: 3,
            monthlyLimit: 1);
        fixture.AddPlan("Four days, cheaper", 1500m, 4, active: true);
        fixture.AddPlan("Four days", 1000m, 4, active: true);
        fixture.AddPlan("Five days", 500m, 5, active: true);
        fixture.AddPlan("Inactive", 500m, 20, active: false);

        var result = await fixture.Service.CheckInSelfAsync(fixture.Student.Id, "12345678");
        var notice = Assert.Single(fixture.AttendanceRepository.Notifications);

        Assert.Equal(AttendanceQuotaNotificationStatus.Pending, result.QuotaNoticeStatus);
        Assert.Equal("Four days", notice.RecommendedPlanName);
        Assert.Equal(4, notice.RecommendedMonthlyDays);
        Assert.Equal(1000m, notice.RecommendedPrice);
    }

    [Fact]
    public async Task Quota_notice_without_a_higher_active_plan_keeps_recommendation_empty()
    {
        var fixture = new Fixture(
            new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero),
            weeklyLimit: 3,
            monthlyLimit: 1);

        await fixture.Service.CheckInSelfAsync(fixture.Student.Id, "12345678");
        var notice = Assert.Single(fixture.AttendanceRepository.Notifications);

        Assert.Null(notice.RecommendedPlanId);
        Assert.Null(notice.RecommendedPlanName);
        Assert.Null(notice.RecommendedMonthlyDays);
        Assert.Null(notice.RecommendedPrice);
    }

    [Fact]
    public async Task Check_in_captures_the_clock_once_and_retry_returns_the_original_attendance()
    {
        var capturedAt = DateTimeOffset.Parse("2026-11-05T02:59:59Z");
        var fixture = new Fixture(capturedAt, weeklyLimit: 3, monthlyLimit: 12, shiftClockDuringTransaction: true);
        var kiosk = KioskSession.Create(
            "Front desk", "cekide-app", "teacher-subject", "activation-hash", capturedAt.AddMinutes(-2));
        EntityIds.Assign(kiosk, 1);
        kiosk.Activate("activation-hash", "token-hash", capturedAt.AddMinutes(-1));
        fixture.KioskRepository.Items.Add(kiosk);

        var first = await fixture.Service.CheckInKioskAsync(fixture.Student.Id, "12345678", kiosk.Id, "cekide-app");
        var originalTimestamp = first.Attendance.CheckedInAt;
        var originalSource = first.Attendance.Source;

        fixture.Clock.Current = capturedAt;
        var duplicate = await fixture.Service.CheckInSelfAsync(fixture.Student.Id, "12345678");

        Assert.Equal(2, fixture.Clock.Reads);
        Assert.Equal(AttendanceCheckInOutcome.Created, first.Outcome);
        Assert.Equal(AttendanceCheckInOutcome.AlreadyCheckedIn, duplicate.Outcome);
        Assert.Equal(new DateOnly(2026, 11, 4), first.Attendance.LocalDate);
        Assert.Equal(AttendanceSource.Kiosk, originalSource);
        Assert.Equal(originalTimestamp, duplicate.Attendance.CheckedInAt);
        Assert.Equal(originalSource, duplicate.Attendance.Source);
        Assert.Single(fixture.AttendanceRepository.Items);
        Assert.Empty(fixture.AttendanceRepository.Notifications);
    }

    [Fact]
    public async Task Self_check_in_rejects_another_students_dni_without_writing()
    {
        var fixture = new Fixture(DateTimeOffset.Parse("2026-11-05T15:00:00Z"), weeklyLimit: 3, monthlyLimit: 12);

        var error = await Assert.ThrowsAsync<ForbiddenException>(
            () => fixture.Service.CheckInSelfAsync(fixture.Student.Id, "87654321"));

        Assert.Contains("does not match", error.Message);
        Assert.Empty(fixture.AttendanceRepository.Items);
        Assert.Empty(fixture.AttendanceRepository.Periods);
    }

    [Fact]
    public async Task Thirteenth_attendance_is_allowed_without_creating_a_second_quota_notice()
    {
        var fixture = new Fixture(DateTimeOffset.Parse("2026-11-12T15:00:00Z"), weeklyLimit: 7, monthlyLimit: 12);
        for (var day = 1; day <= 11; day++)
        {
            fixture.SeedAttendance(new DateOnly(2026, 11, day));
        }

        var twelfth = await fixture.Service.CheckInSelfAsync(fixture.Student.Id, "12345678");
        fixture.Clock.Current = DateTimeOffset.Parse("2026-11-13T15:00:00Z");
        var thirteenth = await fixture.Service.CheckInSelfAsync(fixture.Student.Id, "12345678");

        Assert.Equal(12, twelfth.Summary.Month.Consumed);
        Assert.Equal(AttendanceQuotaNotificationStatus.Pending, twelfth.QuotaNoticeStatus);
        Assert.Equal(13, thirteenth.Summary.Month.Consumed);
        Assert.Equal(1, thirteenth.Summary.Month.Exceeded);
        Assert.Equal(AttendanceQuotaNotificationStatus.Pending, thirteenth.QuotaNoticeStatus);
        Assert.Single(fixture.AttendanceRepository.Notifications);
        Assert.True(fixture.AttendanceRepository.Items.Count == 13);
    }

    private sealed class Fixture
    {
        public Fixture(
            DateTimeOffset now,
            int weeklyLimit,
            int monthlyLimit,
            bool shiftClockDuringTransaction = false,
            IShiftRepository? shifts = null)
        {
            WeeklyLimit = weeklyLimit;
            MonthlyLimit = monthlyLimit;
            Clock = new CountingClock(now);
            Student = Students.Seed("Student Name", "student@example.test", planId: 1, new DateOnly(2026, 11, 1));
            Student.Update(Student.FullName, Student.Email, null, "12345678");

            var plan = Plan.Create("Three days", 1000m);
            EntityIds.Assign(plan, 1);
            Plans.Items.Add(plan);
            var limits = PlanAttendanceLimits.CreateInitial(1, weeklyLimit, monthlyLimit, "owner-test", new DateOnly(2026, 11, 1));
            EntityIds.Assign(limits, 1);
            Plans.AttendanceLimits.Add(limits);

            AttendanceRepository = new InMemoryAttendanceRepository();
            KioskRepository = new InMemoryKioskSessionRepository();
            Transaction = new TestAttendanceTransaction(Clock, shiftClockDuringTransaction);
            Service = new AttendanceCheckInService(
                Students,
                Plans,
                AttendanceRepository,
                KioskRepository,
                Transaction,
                new FakeUnitOfWork(),
                Clock,
                new AttendanceQuotaNotificationService(
                    Plans,
                    AttendanceRepository,
                    new InMemoryAttendanceQuotaNotificationRepository(),
                    new FakeEmailSender(),
                    Clock),
                shifts);
        }

        public void AddPlan(string name, decimal price, int monthlyDays, bool active)
        {
            var plan = Plan.Create(name, price);
            if (!active)
            {
                plan.Deactivate();
            }

            Plans.Add(plan);
            var limits = PlanAttendanceLimits.CreateInitial(
                plan.Id,
                weeklyDays: 3,
                monthlyDays: monthlyDays,
                configuredBy: "owner-test",
                today: new DateOnly(2026, 11, 1));
            Plans.AddAttendanceLimits(limits);
        }

        public CountingClock Clock { get; }
        private int WeeklyLimit { get; }
        private int MonthlyLimit { get; }
        public InMemoryStudentRepository Students { get; } = new();
        public InMemoryPlanRepository Plans { get; } = new();
        public InMemoryAttendanceRepository AttendanceRepository { get; }
        public InMemoryKioskSessionRepository KioskRepository { get; }
        public TestAttendanceTransaction Transaction { get; }
        public AttendanceCheckInService Service { get; }
        public Student Student { get; }

        public void SeedAttendance(DateOnly date)
        {
            var periodName = PaymentPeriod.From(date).ToString();
            var period = AttendanceRepository.Periods.FirstOrDefault(candidate => candidate.Period == periodName);
            if (period is null)
            {
                period = AttendancePeriod.Create(
                    Student.Id, 1, 1, "Three days", WeeklyLimit, MonthlyLimit, date);
                EntityIds.Assign(period, 1);
                AttendanceRepository.Periods.Add(period);
            }

            var checkIn = new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero);
            var attendance = CekideApi.Domain.Attendance.Create(
                Student.Id,
                period.Id,
                date,
                checkIn,
                AttendanceSource.Self,
                null,
                new AttendanceQuotaSnapshot(1, 1, false, false, false));
            EntityIds.Assign(attendance, AttendanceRepository.Items.Count + 1);
            AttendanceRepository.Items.Add(attendance);
        }
    }

    private sealed class FullShiftRepository : IShiftRepository
    {
        public int OccupancyReads { get; private set; }

        public Task<IReadOnlyList<ShiftListData>> ListAsync(
            DateOnly fromInclusive,
            DateOnly toExclusive,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GymShift?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int?> FindMatchingOpenShiftAsync(
            DateOnly localDate,
            TimeOnly localTime,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(1);

        public Task<GymShift> CreateAsync(GymShift shift, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GymShift> CancelAsync(
            int id,
            string actor,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ShiftOccupancyData> GetOccupancyAsync(int id, CancellationToken cancellationToken = default)
        {
            OccupancyReads++;
            throw new NotSupportedException();
        }

        public Task<HabitualOccupancyResult> AddHabitualAsync(
            int shiftId,
            int studentId,
            string actor,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ShiftHabitualOccupancy> RemoveHabitualAsync(
            int shiftId,
            int occupancyId,
            string actor,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Current { get; set; } = now;
        public int Reads { get; private set; }

        public DateTimeOffset UtcNow
        {
            get
            {
                Reads++;
                return Current;
            }
        }
    }

    private sealed class InMemoryKioskSessionRepository : IKioskSessionRepository
    {
        public List<KioskSession> Items { get; } = [];

        public Task<KioskSession?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(session => session.Id == id));

        public Task<KioskSession?> GetByActivationCodeHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(session => session.ActivationCodeHash == hash));

        public Task<KioskSession?> GetByTokenHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(session => session.TokenHash == hash));

        public Task<IReadOnlyList<KioskSession>> ListForTenantAsync(string tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KioskSession>>(Items.Where(session => session.Tenant == tenant).ToArray());

        public Task<bool> IsRevokedAsync(int id, string tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(session => session.Id == id && session.Tenant == tenant && session.RevokedAt is not null));

        public void Add(KioskSession session) => Items.Add(session);
    }

    private sealed class TestAttendanceTransaction(CountingClock clock, bool shiftClock) : IAttendanceTransaction
    {
        public async Task<T> ExecuteForStudentAsync<T>(
            int studentId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            if (shiftClock)
            {
                clock.Current = DateTimeOffset.Parse("2026-11-05T03:00:00Z");
            }

            return await action(cancellationToken);
        }
    }

    private sealed class InMemoryAttendanceRepository : IAttendanceRepository
    {
        private int _nextPeriodId = 1;
        private int _nextAttendanceId = 1;
        private int _nextNotificationId = 1;

        public List<CekideApi.Domain.Attendance> Items { get; } = [];
        public List<AttendancePeriod> Periods { get; } = [];
        public List<AttendanceQuotaNotification> Notifications { get; } = [];
        public Task<CekideApi.Domain.Attendance?> GetForStudentDateAsync(
            int studentId, DateOnly localDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item.StudentId == studentId && item.LocalDate == localDate));

        public Task<AttendancePeriod?> GetPeriodAsync(
            int studentId, string period, CancellationToken cancellationToken = default) =>
            Task.FromResult(Periods.FirstOrDefault(item => item.StudentId == studentId && item.Period == period));

        public Task<IReadOnlyList<DateOnly>> GetAttendanceDatesAsync(
            int studentId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DateOnly>>(Items
                .Where(item => item.StudentId == studentId && item.LocalDate >= fromInclusive && item.LocalDate < toExclusive)
                .Select(item => item.LocalDate)
                .ToArray());

        public Task<IReadOnlyList<CekideApi.Domain.Attendance>> GetAttendancesAsync(
            int studentId, DateOnly fromInclusive, DateOnly toExclusive, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CekideApi.Domain.Attendance>>(Items
                .Where(item => item.StudentId == studentId && item.LocalDate >= fromInclusive && item.LocalDate < toExclusive)
                .OrderBy(item => item.LocalDate)
                .ToArray());

        public Task<AttendanceQuotaNotification?> GetQuotaNotificationAsync(
            int studentId, string period, CancellationToken cancellationToken = default) =>
            Task.FromResult(Notifications.FirstOrDefault(item => item.StudentId == studentId && item.Period == period));

        public void AddPeriod(AttendancePeriod period)
        {
            EntityIds.Assign(period, _nextPeriodId++);
            Periods.Add(period);
        }

        public void AddAttendance(CekideApi.Domain.Attendance attendance)
        {
            EntityIds.Assign(attendance, _nextAttendanceId++);
            Items.Add(attendance);
        }

        public void AddQuotaNotification(AttendanceQuotaNotification notification)
        {
            EntityIds.Assign(notification, _nextNotificationId++);
            Notifications.Add(notification);
        }
    }
}
