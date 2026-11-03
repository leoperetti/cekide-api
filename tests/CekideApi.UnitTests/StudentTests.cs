using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class StudentTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static Student NewStudent(int? planId = 1) =>
        Student.Create("Ana Pérez", "ana@example.com", "1155550000", planId, Today);

    [Fact]
    public void Create_normalizes_email_and_trims_text()
    {
        var student = Student.Create("  Ana Pérez ", "  Ana.Perez@Example.COM ", "  1155550000 ", 1, Today);

        Assert.Equal("Ana Pérez", student.FullName);
        Assert.Equal("ana.perez@example.com", student.Email);
        Assert.Equal("1155550000", student.Phone);
        Assert.True(student.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a@@b.com")]
    [InlineData("a b@c.com")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    public void Create_rejects_missing_or_invalid_emails(string? email)
    {
        Assert.Throws<DomainValidationException>(() => Student.Create("Ana", email, null, 1, Today));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_rejects_a_missing_name(string? name)
    {
        Assert.Throws<DomainValidationException>(() => Student.Create(name, "ana@example.com", null, 1, Today));
    }

    [Fact]
    public void Blank_phone_is_stored_as_null()
    {
        Assert.Null(Student.Create("Ana", "ana@example.com", "   ", 1, Today).Phone);
    }

    [Fact]
    public void NormalizeEmail_makes_equivalent_addresses_equal()
    {
        Assert.Equal(Student.NormalizeEmail(" Ana@Example.com"), Student.NormalizeEmail("ana@example.COM "));
    }

    [Fact]
    public void Update_applies_the_same_validation_as_create()
    {
        var student = NewStudent();

        student.Update("Ana María", "NEW@example.com", null);

        Assert.Equal("Ana María", student.FullName);
        Assert.Equal("new@example.com", student.Email);
        Assert.Throws<DomainValidationException>(() => student.Update("Ana", "", null));
    }

    [Fact]
    public void Deactivate_keeps_the_student_data_and_history_fields()
    {
        var student = NewStudent(planId: 7);

        student.Deactivate();

        Assert.False(student.IsActive);
        Assert.Equal("ana@example.com", student.Email);
        Assert.Equal(7, student.PlanId);
        Assert.Equal(Today, student.MonthlyMembershipStartedOn);
    }

    [Fact]
    public void A_student_created_with_a_plan_starts_the_monthly_membership_today()
    {
        var student = NewStudent(planId: 3);

        Assert.Equal(3, student.PlanId);
        Assert.Equal(Today, student.MonthlyMembershipStartedOn);
        Assert.True(student.HasMonthlyMembership);
    }

    [Fact]
    public void A_student_created_without_a_plan_only_has_short_term_passes()
    {
        var student = NewStudent(planId: null);

        Assert.Null(student.PlanId);
        Assert.Null(student.MonthlyMembershipStartedOn);
        Assert.False(student.HasMonthlyMembership);
    }

    [Fact]
    public void Assigning_a_first_plan_starts_the_monthly_membership_on_that_day()
    {
        var student = NewStudent(planId: null);
        var switchDay = new DateOnly(2026, 11, 15);

        student.ChangePlan(5, switchDay);

        Assert.Equal(5, student.PlanId);
        Assert.Equal(switchDay, student.MonthlyMembershipStartedOn);
        Assert.Equal(5, student.PlanIdOn(switchDay));
    }

    [Fact]
    public void Changing_plan_applies_from_the_next_month()
    {
        var student = NewStudent(planId: 1);

        student.ChangePlan(2, new DateOnly(2026, 11, 20));

        Assert.Equal(1, student.PlanIdOn(new DateOnly(2026, 11, 20)));
        Assert.Equal(1, student.PlanIdOn(new DateOnly(2026, 11, 30)));
        Assert.Equal(2, student.PlanIdOn(new DateOnly(2026, 12, 1)));
        Assert.Equal(2, student.PlanIdOn(new DateOnly(2027, 3, 1)));
        Assert.Equal(Today, student.MonthlyMembershipStartedOn);
    }

    [Fact]
    public void Changing_plan_in_december_takes_effect_in_january()
    {
        var student = NewStudent(planId: 1);

        student.ChangePlan(2, new DateOnly(2026, 12, 31));

        Assert.Equal(1, student.PlanIdOn(new DateOnly(2026, 12, 31)));
        Assert.Equal(2, student.PlanIdOn(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void A_second_change_in_the_same_month_replaces_the_pending_plan()
    {
        var student = NewStudent(planId: 1);

        student.ChangePlan(2, new DateOnly(2026, 11, 10));
        student.ChangePlan(3, new DateOnly(2026, 11, 25));

        Assert.Equal(1, student.PlanIdOn(new DateOnly(2026, 11, 30)));
        Assert.Equal(3, student.PlanIdOn(new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void A_change_made_after_a_pending_plan_took_effect_keeps_the_applied_one_until_next_month()
    {
        var student = NewStudent(planId: 1);
        student.ChangePlan(2, new DateOnly(2026, 11, 10));

        student.ChangePlan(3, new DateOnly(2026, 12, 5));

        Assert.Equal(2, student.PlanIdOn(new DateOnly(2026, 12, 5)));
        Assert.Equal(3, student.PlanIdOn(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Re_assigning_the_current_plan_cancels_a_pending_change()
    {
        var student = NewStudent(planId: 1);
        student.ChangePlan(2, new DateOnly(2026, 11, 10));

        student.ChangePlan(1, new DateOnly(2026, 11, 12));

        Assert.Equal(1, student.PlanIdOn(new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void Replacing_the_routine_keeps_only_the_current_one()
    {
        var student = NewStudent();
        var first = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
        var second = first.AddDays(2);

        student.SetRoutine("students/1/routine.pdf", "old.pdf", "teacher-1", first);
        student.SetRoutine("students/1/routine.pdf", "new.pdf", "owner-1", second);

        Assert.Equal("new.pdf", student.RoutineFileName);
        Assert.Equal("owner-1", student.RoutineUploadedBy);
        Assert.Equal(second, student.RoutineUploadedAt);
        Assert.Equal("students/1/routine.pdf", student.RoutineBlobPath);
        Assert.True(student.HasRoutine);
    }

    [Fact]
    public void A_new_student_has_no_routine()
    {
        Assert.False(NewStudent().HasRoutine);
    }
}
