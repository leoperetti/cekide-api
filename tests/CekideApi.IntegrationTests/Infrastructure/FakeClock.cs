using CekideApi.Application.Abstractions;

namespace CekideApi.IntegrationTests.Infrastructure;

public sealed class FakeClock : IClock
{
    // 2026-11-05 12:00 in Buenos Aires: inside the payment window (day 5).
    public static readonly DateTimeOffset DefaultNow = new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);

    public FakeClock(DateTimeOffset? now = null) => UtcNow = now ?? DefaultNow;

    public DateTimeOffset UtcNow { get; private set; }

    public void Set(DateTimeOffset now) => UtcNow = now;

    /// <summary>Sets the clock to noon (Buenos Aires) of the given local date.</summary>
    public void SetBuenosAiresDate(int year, int month, int day) =>
        UtcNow = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.FromHours(-3)).ToUniversalTime();
}
