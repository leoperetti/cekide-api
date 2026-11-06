using CekideApi.Domain;

namespace CekideApi.UnitTests;

public class KioskSessionTests
{
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-11-05T12:00:00Z");

    [Fact]
    public void Activation_code_can_be_used_once_and_token_expires_after_twelve_hours()
    {
        var session = CreateSession();

        session.Activate("activation-hash", "token-hash", CreatedAt.AddMinutes(2));

        Assert.True(session.HasValidTokenHash("token-hash", CreatedAt.AddHours(1)));
        Assert.False(session.HasValidTokenHash("activation-hash", CreatedAt.AddHours(1)));
        Assert.False(session.IsActive(CreatedAt.AddHours(12).AddMinutes(3)));
        Assert.Throws<ConflictException>(() =>
            session.Activate("activation-hash", "another-token", CreatedAt.AddMinutes(3)));
    }

    [Fact]
    public void Activation_code_expires_ten_minutes_after_creation()
    {
        var session = CreateSession();

        Assert.Throws<ConflictException>(() =>
            session.Activate("activation-hash", "token-hash", CreatedAt.AddMinutes(10)));
    }

    [Fact]
    public void Revocation_is_idempotent_and_immediately_invalidates_the_token()
    {
        var session = CreateSession();
        session.Activate("activation-hash", "token-hash", CreatedAt.AddMinutes(1));

        session.Revoke("teacher-subject", CreatedAt.AddMinutes(2));
        session.Revoke("another-subject", CreatedAt.AddMinutes(3));

        Assert.False(session.HasValidTokenHash("token-hash", CreatedAt.AddMinutes(3)));
        Assert.Equal("teacher-subject", session.RevokedBy);
        Assert.Equal(CreatedAt.AddMinutes(2).UtcDateTime, session.RevokedAt);
    }

    private static KioskSession CreateSession() =>
        KioskSession.Create("Front desk", "cekide-app", "teacher-subject", "activation-hash", CreatedAt);
}
