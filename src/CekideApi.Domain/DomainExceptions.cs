namespace CekideApi.Domain;

public abstract class DomainException(string message) : Exception(message);

public sealed class NotFoundException(string message) : DomainException(message);

public sealed class ForbiddenException(string message) : DomainException(message);

public sealed class ConflictException(string message) : DomainException(message);

public sealed class DomainValidationException(string message) : DomainException(message);

public sealed class PayloadTooLargeException(string message) : DomainException(message);

/// <summary>A dependency (such as authcraft-api) failed or is unreachable.</summary>
public sealed class UpstreamServiceException(string message) : DomainException(message);

public sealed class AttendanceException : DomainException
{
    public AttendanceException(int statusCode, string code, string message) : base(message)
    {
        if (statusCode is < 400 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }

        if (string.IsNullOrWhiteSpace(code) || code.Any(c => c is not (>= 'a' and <= 'z') and not '_'))
        {
            throw new ArgumentException("Error code must contain lowercase ASCII letters or underscores.", nameof(code));
        }

        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
