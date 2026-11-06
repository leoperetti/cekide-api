namespace CekideApi.Domain;

public sealed record StudentInvitationProfile(
    int StudentId,
    string? FirstName,
    string? LastName,
    string FullName,
    string? PhoneNumber,
    string? Dni);

public sealed record AcceptedStudentProfile(
    long InvitationId,
    int StudentId,
    string IdentityId,
    DateTimeOffset AcceptedAt,
    StudentProfileSnapshot Prefill,
    StudentRegisteredProfile Registered);

public sealed record StudentProfileSnapshot(
    string? FirstName,
    string? LastName,
    string FullName,
    string? PhoneNumber,
    string? Dni);

public sealed record StudentRegisteredProfile(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string Dni);
