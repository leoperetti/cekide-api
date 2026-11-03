using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class PaymentEntitiesTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 5);

    private static MonthlyPayment NewPayment(decimal amount = 15000m) =>
        MonthlyPayment.Create(7, PaymentPeriod.Parse("2026-11"), amount, Today, "teacher-1", Now);

    [Fact]
    public void A_monthly_payment_is_created_paid_with_its_snapshot_and_registration_data()
    {
        var payment = NewPayment();

        Assert.Equal(7, payment.StudentId);
        Assert.Equal("2026-11", payment.Period);
        Assert.Equal(15000m, payment.Amount);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(Today, payment.PaidOn);
        Assert.Equal("teacher-1", payment.RegisteredBy);
        Assert.Equal(Now, payment.RegisteredAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.005)]
    public void A_monthly_payment_needs_a_valid_amount(double amount)
    {
        Assert.Throws<DomainValidationException>(() => NewPayment((decimal)amount));
    }

    [Fact]
    public void Modify_changes_amount_and_date_of_a_paid_payment()
    {
        var payment = NewPayment();

        payment.Modify(16000m, new DateOnly(2026, 11, 6));

        Assert.Equal(16000m, payment.Amount);
        Assert.Equal(new DateOnly(2026, 11, 6), payment.PaidOn);
        Assert.Throws<DomainValidationException>(() => payment.Modify(0m, null));
    }

    [Fact]
    public void Modify_with_nothing_to_change_is_rejected()
    {
        Assert.Throws<DomainValidationException>(() => NewPayment().Modify(null, null));
    }

    [Fact]
    public void Void_marks_the_payment_voided_once_and_blocks_later_changes()
    {
        var payment = NewPayment();

        payment.Void();

        Assert.Equal(PaymentStatus.Voided, payment.Status);
        Assert.Throws<ConflictException>(() => payment.Void());
        Assert.Throws<ConflictException>(() => payment.Modify(1m, null));
    }

    [Fact]
    public void A_short_term_pass_is_created_paid_with_its_validity()
    {
        var pass = ShortTermPass.Create(7, PassType.Week, 12000m, new DateOnly(2026, 11, 10), "teacher-1", Now);

        Assert.Equal(PassType.Week, pass.Type);
        Assert.Equal(new DateOnly(2026, 11, 10), pass.ValidFrom);
        Assert.Equal(new DateOnly(2026, 11, 16), pass.ValidUntil);
        Assert.Equal(PaymentStatus.Paid, pass.Status);
        Assert.Equal(12000m, pass.Amount);
    }

    [Fact]
    public void A_pass_needs_a_valid_amount_and_type()
    {
        Assert.Throws<DomainValidationException>(() => ShortTermPass.Create(7, PassType.Day, 0m, Today, "t", Now));
        Assert.Throws<DomainValidationException>(() => ShortTermPass.Create(7, (PassType)42, 10m, Today, "t", Now));
    }

    [Fact]
    public void Pass_modify_validates_the_date_range_and_void_blocks_changes()
    {
        var pass = ShortTermPass.Create(7, PassType.Day, 3000m, Today, "t", Now);

        pass.Modify(3500m, Today.AddDays(1), Today.AddDays(2));

        Assert.Equal(3500m, pass.Amount);
        Assert.Equal(Today.AddDays(1), pass.ValidFrom);
        Assert.Equal(Today.AddDays(2), pass.ValidUntil);
        Assert.Throws<DomainValidationException>(() => pass.Modify(null, Today.AddDays(5), Today.AddDays(2)));

        pass.Void();
        Assert.Equal(PaymentStatus.Voided, pass.Status);
        Assert.Throws<ConflictException>(() => pass.Void());
    }

    [Fact]
    public void History_rows_capture_who_when_and_the_values()
    {
        var entry = PaymentHistory.Record(
            PaymentKind.Monthly, 12, PaymentAction.Modified, "{\"amount\":1}", "{\"amount\":2}", "owner-1", Now);

        Assert.Equal(PaymentKind.Monthly, entry.PaymentKind);
        Assert.Equal(12, entry.PaymentId);
        Assert.Equal(PaymentAction.Modified, entry.Action);
        Assert.Equal("{\"amount\":1}", entry.OldValues);
        Assert.Equal("{\"amount\":2}", entry.NewValues);
        Assert.Equal("owner-1", entry.PerformedBy);
        Assert.Equal(Now, entry.PerformedAt);
    }
}
