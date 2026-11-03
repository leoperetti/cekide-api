using CekideApi.Application.Abstractions;
using CekideApi.Application.Payments;
using CekideApi.Domain;

namespace CekideApi.Application.Students;

public sealed class StudentService(
    IStudentRepository students,
    IPlanRepository plans,
    IUnitOfWork unitOfWork,
    IClock clock,
    PaymentService payments,
    IStudentInvitationRepository invitations)
{
    public async Task<IReadOnlyList<StudentDto>> ListAsync(string? search, bool? active, CancellationToken cancellationToken = default)
    {
        var found = await students.SearchAsync(search?.Trim(), active, cancellationToken);
        return await MapAsync(found, cancellationToken);
    }

    public async Task<StudentDto> GetAsync(int studentId, CancellationToken cancellationToken = default)
    {
        var student = await RequireAsync(studentId, cancellationToken);
        return (await MapAsync([student], cancellationToken)).Single();
    }

    public async Task<StudentDto> CreateAsync(CreateStudentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PlanId is { } planId)
        {
            await RequireAssignablePlanAsync(planId, cancellationToken);
        }

        var student = Student.Create(request.FullName, request.Email, request.Phone, request.PlanId, Today());
        await EnsureEmailAvailableAsync(student.Email, null, cancellationToken);

        students.Add(student);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await MapAsync([student], cancellationToken)).Single();
    }

    public async Task<StudentDto> UpdateAsync(int studentId, UpdateStudentRequest request, CancellationToken cancellationToken = default)
    {
        var student = await RequireAsync(studentId, cancellationToken);

        student.Update(request.FullName, request.Email, request.Phone);
        await EnsureEmailAvailableAsync(student.Email, student.Id, cancellationToken);

        if (request.PlanId is { } planId)
        {
            if (student.PlanIdOn(Today()) != planId)
            {
                await RequireAssignablePlanAsync(planId, cancellationToken);
            }

            student.ChangePlan(planId, Today());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return (await MapAsync([student], cancellationToken)).Single();
    }

    public async Task DeactivateAsync(int studentId, CancellationToken cancellationToken = default)
    {
        var student = await RequireAsync(studentId, cancellationToken);
        student.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private DateOnly Today() => GymCalendar.Today(clock.UtcNow);

    private async Task<Student> RequireAsync(int studentId, CancellationToken cancellationToken) =>
        await students.GetByIdAsync(studentId, cancellationToken)
        ?? throw new NotFoundException($"Student {studentId} was not found.");

    private async Task EnsureEmailAvailableAsync(string email, int? exceptStudentId, CancellationToken cancellationToken)
    {
        if (await students.EmailExistsAsync(email, exceptStudentId, cancellationToken))
        {
            throw new ConflictException("Another student already uses this email.");
        }
    }

    private async Task RequireAssignablePlanAsync(int planId, CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(planId, cancellationToken)
                   ?? throw new DomainValidationException($"Plan {planId} does not exist.");
        if (!plan.IsActive)
        {
            throw new DomainValidationException($"Plan '{plan.Name}' is not active.");
        }
    }

    private async Task<IReadOnlyList<StudentDto>> MapAsync(IReadOnlyList<Student> list, CancellationToken cancellationToken)
    {
        var today = Today();
        var planIds = list.Select(s => s.PlanIdOn(today)).OfType<int>().Distinct().ToList();
        var names = (await plans.GetByIdsAsync(planIds, cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
        var statuses = await payments.CurrentMonthStatusesAsync(list, cancellationToken);
        var invitationStatuses = await invitations.LatestStatusesAsync(list.Select(s => s.Id), cancellationToken);

        return list.Select(student =>
        {
            var planId = student.PlanIdOn(today);
            var pendingInFuture = student.PendingPlanEffectiveFrom is { } from && from > today;
            return new StudentDto(
                student.Id,
                student.FullName,
                student.Email,
                student.Phone,
                planId,
                planId is { } id && names.TryGetValue(id, out var name) ? name : null,
                pendingInFuture ? student.PendingPlanId : null,
                pendingInFuture ? student.PendingPlanEffectiveFrom : null,
                student.MonthlyMembershipStartedOn,
                student.IsActive,
                student.HasRoutine,
                student.RoutineFileName,
                student.RoutineUploadedAt,
                student.RoutineUploadedBy,
                PaymentStatus: statuses[student.Id],
                IdentityId: student.IdentityId,
                InvitationStatus: invitationStatuses.TryGetValue(student.Id, out var invitation) ? invitation : null);
        }).ToList();
    }
}
