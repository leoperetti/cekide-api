namespace CekideApi.Domain;

public abstract class DomainException(string message) : Exception(message);

public sealed class NotFoundException(string message) : DomainException(message);

public sealed class ForbiddenException(string message) : DomainException(message);

public sealed class ConflictException(string message) : DomainException(message);

public sealed class DomainValidationException(string message) : DomainException(message);

public sealed class PayloadTooLargeException(string message) : DomainException(message);

/// <summary>A dependency (such as authcraft-api) failed or is unreachable.</summary>
public sealed class UpstreamServiceException(string message) : DomainException(message);
