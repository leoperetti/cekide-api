using CekideApi.Domain;

namespace CekideApi.Application.Students;

public sealed record CreateStudentRequest(string? FullName, string? Email, string? Phone, int? PlanId);

/// <summary>PlanId = null leaves the current plan unchanged.</summary>
public sealed record UpdateStudentRequest(string? FullName, string? Email, string? Phone, int? PlanId);

/// <param name="PaymentStatus">paid | pending | overdue for the current month; null when the student has no monthly obligation.</param>
/// <param name="IdentityId">authcraft identity once the student accepted the invitation; null while unlinked.</param>
/// <param name="InvitationStatus">sent | accepted | expired from the latest invitation; null when never invited.</param>
public sealed record StudentDto(
    int Id,
    string FullName,
    string Email,
    string? Phone,
    int? PlanId,
    string? PlanName,
    int? PendingPlanId,
    DateOnly? PendingPlanEffectiveFrom,
    DateOnly? MonthlyMembershipStartedOn,
    bool IsActive,
    bool HasRoutine,
    string? RoutineFileName,
    DateTime? RoutineUploadedAt,
    string? RoutineUploadedBy,
    string? PaymentStatus,
    string? IdentityId,
    InvitationStatus? InvitationStatus);

public sealed record RoutineDownload(Stream Content, string FileName);
