using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class PaymentRulesTests
{
    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    private static Student MonthlyStudent(DateOnly startedOn, int planId = 1) =>
        Student.Create("Ana", "ana@example.com", null, planId, startedOn);

    private static PaymentPeriod P(string value) => PaymentPeriod.Parse(value);

    // ---------- PaymentPeriod ----------

    [Fact]
    public void Period_parses_formats_and_navigates_months()
    {
        var period = P("2026-11");

        Assert.Equal(2026, period.Year);
        Assert.Equal(11, period.Month);
        Assert.Equal("2026-11", period.ToString());
        Assert.Equal(D(11, 1), period.FirstDay);
        Assert.Equal(D(11, 30), period.LastDay);
        Assert.Equal(P("2026-12"), period.Next());
        Assert.Equal(P("2027-01"), P("2026-12").Next());
        Assert.Equal(P("2026-03"), PaymentPeriod.From(D(3, 17)));
        Assert.True(P("2026-10") < P("2026-11"));
        Assert.True(P("2027-01") > P("2026-12"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-1")]
    [InlineData("26-11")]
    [InlineData("2026/11")]
    [InlineData("2026-11-01")]
    [InlineData("abcd-ef")]
    public void Period_rejects_anything_but_YYYY_MM(string? value)
    {
        Assert.Throws<DomainValidationException>(() => PaymentPeriod.Parse(value));
    }

    // ---------- Window: days 1-10 pending, overdue from day 11 ----------

    [Theory]
    [InlineData(1, MonthlyPaymentStatus.Pending)]
    [InlineData(10, MonthlyPaymentStatus.Pending)]
    [InlineData(11, MonthlyPaymentStatus.Overdue)]
    [InlineData(30, MonthlyPaymentStatus.Overdue)]
    public void Unpaid_obligation_is_pending_until_day_10_and_overdue_from_day_11(int day, MonthlyPaymentStatus expected)
    {
        var student = MonthlyStudent(D(10, 1));
        var obligation = PaymentRules.ObligationFor(student, P("2026-11"))!;

        Assert.Equal(D(11, 1), obligation.DueFrom);
        Assert.Equal(D(11, 11), obligation.OverdueFrom);
        Assert.Equal(expected, PaymentRules.StatusOf(obligation, paid: false, D(11, day)));
    }

    [Fact]
    public void A_paid_obligation_is_paid_even_after_the_window()
    {
        var obligation = PaymentRules.ObligationFor(MonthlyStudent(D(10, 1)), P("2026-11"))!;

        Assert.Equal(MonthlyPaymentStatus.Paid, PaymentRules.StatusOf(obligation, paid: true, D(11, 25)));
    }

    [Theory]
    [InlineData("2026-11-11T02:59:59Z", MonthlyPaymentStatus.Pending)] // still day 10 in Buenos Aires
    [InlineData("2026-11-11T03:00:00Z", MonthlyPaymentStatus.Overdue)] // day 11 in Buenos Aires
    public void The_window_is_evaluated_in_the_buenos_aires_time_zone(string utcNow, MonthlyPaymentStatus expected)
    {
        var obligation = PaymentRules.ObligationFor(MonthlyStudent(D(10, 1)), P("2026-11"))!;
        var today = GymCalendar.Today(DateTimeOffset.Parse(utcNow));

        Assert.Equal(expected, PaymentRules.StatusOf(obligation, paid: false, today));
    }

    [Fact]
    public void An_unpaid_month_becomes_overdue_when_the_month_changes_and_the_new_month_starts_pending()
    {
        var student = MonthlyStudent(D(9, 1));
        var november = PaymentRules.ObligationFor(student, P("2026-11"))!;
        var december = PaymentRules.ObligationFor(student, P("2026-12"))!;
        var firstOfDecember = GymCalendar.Today(DateTimeOffset.Parse("2026-12-01T03:00:00Z"));

        Assert.Equal(D(12, 1), firstOfDecember);
        Assert.Equal(MonthlyPaymentStatus.Overdue, PaymentRules.StatusOf(november, false, firstOfDecember));
        Assert.Equal(MonthlyPaymentStatus.Pending, PaymentRules.StatusOf(december, false, firstOfDecember));
        Assert.Equal(MonthlyPaymentStatus.Pending, PaymentRules.StatusOf(december, false, D(11, 30)));
    }

    [Fact]
    public void The_year_boundary_works_for_december_obligations()
    {
        var obligation = PaymentRules.ObligationFor(MonthlyStudent(D(1, 1)), P("2026-12"))!;

        Assert.Equal(MonthlyPaymentStatus.Pending, PaymentRules.StatusOf(obligation, false, D(12, 10)));
        Assert.Equal(MonthlyPaymentStatus.Overdue, PaymentRules.StatusOf(obligation, false, D(12, 11)));
        Assert.Equal(MonthlyPaymentStatus.Overdue, PaymentRules.StatusOf(obligation, false, D(1, 2, 2027)));
    }

    // ---------- Monthly membership start ----------

    [Fact]
    public void There_is_no_obligation_for_months_before_the_membership_started()
    {
        var student = MonthlyStudent(D(11, 5));

        Assert.Null(PaymentRules.ObligationFor(student, P("2026-10")));
        Assert.Null(PaymentRules.ObligationFor(student, P("2026-01")));
        Assert.NotNull(PaymentRules.ObligationFor(student, P("2026-11")));
        Assert.NotNull(PaymentRules.ObligationFor(student, P("2027-02")));
    }

    [Fact]
    public void Starting_the_membership_inside_the_window_keeps_the_regular_overdue_day()
    {
        var obligation = PaymentRules.ObligationFor(MonthlyStudent(D(11, 10)), P("2026-11"))!;

        Assert.Equal(D(11, 10), obligation.DueFrom);
        Assert.Equal(D(11, 11), obligation.OverdueFrom);
        Assert.True(obligation.OverdueNoticeAllowed);
    }

    [Fact]
    public void Starting_the_membership_after_day_10_makes_that_month_due_from_that_day_without_an_overdue_notice()
    {
        var student = MonthlyStudent(D(11, 15));
        var november = PaymentRules.ObligationFor(student, P("2026-11"))!;

        Assert.Equal(D(11, 15), november.DueFrom);
        Assert.False(november.OverdueNoticeAllowed);
        Assert.Equal(MonthlyPaymentStatus.Pending, PaymentRules.StatusOf(november, false, D(11, 15)));
        Assert.Equal(MonthlyPaymentStatus.Pending, PaymentRules.StatusOf(november, false, D(11, 30)));
        Assert.Null(PaymentRules.ObligationFor(student, P("2026-10")));
    }

    [Fact]
    public void After_a_late_start_the_following_months_follow_the_regular_window()
    {
        var student = MonthlyStudent(D(11, 15));
        var december = PaymentRules.ObligationFor(student, P("2026-12"))!;

        Assert.Equal(D(12, 1), december.DueFrom);
        Assert.Equal(D(12, 11), december.OverdueFrom);
        Assert.True(december.OverdueNoticeAllowed);
    }

    [Fact]
    public void Moving_from_short_term_passes_to_the_monthly_membership_starts_the_obligation_that_day()
    {
        var student = Student.Create("Ana", "ana@example.com", null, planId: null, D(10, 1));
        Assert.Null(PaymentRules.ObligationFor(student, P("2026-10")));
        Assert.Null(PaymentRules.ObligationFor(student, P("2026-11")));

        student.ChangePlan(1, D(11, 20));

        var november = PaymentRules.ObligationFor(student, P("2026-11"))!;
        Assert.Equal(D(11, 20), november.DueFrom);
        Assert.False(november.OverdueNoticeAllowed);
        Assert.Null(PaymentRules.ObligationFor(student, P("2026-10")));
    }

    // ---------- Who has an obligation ----------

    [Fact]
    public void Students_with_only_short_term_passes_are_never_due_or_overdue()
    {
        var student = Student.Create("Ana", "ana@example.com", null, planId: null, D(11, 1));

        foreach (var period in new[] { "2026-11", "2026-12", "2027-06" })
        {
            Assert.Null(PaymentRules.ObligationFor(student, P(period)));
        }
    }

    [Fact]
    public void Inactive_students_do_not_have_monthly_obligations()
    {
        var student = MonthlyStudent(D(10, 1));
        student.Deactivate();

        Assert.Null(PaymentRules.ObligationFor(student, P("2026-11")));
    }

    // ---------- Amounts and passes ----------

    [Fact]
    public void The_amount_must_equal_the_plan_price_snapshot()
    {
        PaymentRules.EnsureAmountMatchesPrice(15000m, 15000m);
        PaymentRules.EnsureAmountMatchesPrice(15000.00m, 15000m);

        Assert.Throws<DomainValidationException>(() => PaymentRules.EnsureAmountMatchesPrice(7500m, 15000m));
        Assert.Throws<DomainValidationException>(() => PaymentRules.EnsureAmountMatchesPrice(15000.01m, 15000m));
        Assert.Throws<DomainValidationException>(() => PaymentRules.EnsureAmountMatchesPrice(0m, 15000m));
    }

    [Theory]
    [InlineData(PassType.Day, 0)]
    [InlineData(PassType.Week, 6)]
    [InlineData(PassType.FifteenDays, 14)]
    public void Pass_validity_includes_the_start_day_and_the_pass_length(PassType type, int extraDays)
    {
        var (from, until) = PaymentRules.PassValidity(type, D(11, 10));

        Assert.Equal(D(11, 10), from);
        Assert.Equal(D(11, 10).AddDays(extraDays), until);
    }
}
