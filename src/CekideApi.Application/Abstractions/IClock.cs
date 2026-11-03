namespace CekideApi.Application.Abstractions;

/// <summary>Injectable time source so payment windows and jobs are testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
