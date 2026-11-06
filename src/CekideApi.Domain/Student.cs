using System.Text.RegularExpressions;

namespace CekideApi.Domain;

public sealed partial class Student : Entity
{
    public const int MaxNameLength = 150;
    public const int MaxEmailLength = 254;
    public const int MaxPhoneLength = 30;
    public const int MaxIdentityIdLength = 100;
    public const int MaxDniLength = 8;

    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    private Student()
    {
    }

    public string FullName { get; private set; } = string.Empty;

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    public string? Dni { get; private set; }

    /// <summary>Required, valid, unique and stored trimmed and lower-cased.</summary>
    public string Email { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    /// <summary>Current monthly plan; null while the student only buys short-term passes.</summary>
    public int? PlanId { get; private set; }

    public int? PendingPlanId { get; private set; }

    public DateOnly? PendingPlanEffectiveFrom { get; private set; }

    /// <summary>Day the monthly obligation started (the day the student switched to the monthly membership).</summary>
    public DateOnly? MonthlyMembershipStartedOn { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>authcraft identity ("sub"); filled when the student accepts an invitation.</summary>
    public string? IdentityId { get; private set; }

    public string? RoutineBlobPath { get; private set; }

    public string? RoutineFileName { get; private set; }

    public DateTime? RoutineUploadedAt { get; private set; }

    public string? RoutineUploadedBy { get; private set; }

    public bool HasMonthlyMembership => PlanId is not null;

    public bool HasRoutine => RoutineBlobPath is not null;

    public static string NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new DomainValidationException("Email is required.");
        }

        if (normalized.Length > MaxEmailLength || !EmailPattern().IsMatch(normalized))
        {
            throw new DomainValidationException("Email is not valid.");
        }

        return normalized;
    }

    public static string NormalizeDni(string? dni)
    {
        var normalized = dni?.Replace(".", string.Empty).Replace(" ", string.Empty);
        if (normalized is null || normalized.Length is < 7 or > MaxDniLength ||
            normalized.Any(c => c is < '0' or > '9'))
        {
            throw new DomainValidationException("DNI must contain seven or eight ASCII digits, optionally separated by dots or spaces.");
        }

        return normalized;
    }

    public static Student Create(string? fullName, string? email, string? phone, int? planId, DateOnly today,
        string? dni = null, string? firstName = null, string? lastName = null)
    {
        var student = new Student();
        student.Update(fullName, email, phone, dni, firstName, lastName);
        if (planId is not null)
        {
            student.ChangePlan(planId.Value, today);
        }

        return student;
    }

    public void Update(string? fullName, string? email, string? phone,
        string? dni = null, string? firstName = null, string? lastName = null)
    {
        var name = fullName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new DomainValidationException("Full name is required.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new DomainValidationException($"Full name cannot exceed {MaxNameLength} characters.");
        }

        var first = firstName?.Trim();
        var last = lastName?.Trim();
        var hasStructuredNames = firstName is not null || lastName is not null;
        if (hasStructuredNames)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(last))
            {
                throw new DomainValidationException("First name and last name must be supplied together and cannot be empty.");
            }

            var composed = $"{first} {last}";
            if (composed.Length > MaxNameLength)
            {
                throw new DomainValidationException($"Full name cannot exceed {MaxNameLength} characters.");
            }

            if (name != composed)
            {
                throw new DomainValidationException("Full name must match the supplied first name and last name.");
            }
        }

        var normalizedPhone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (normalizedPhone is { Length: > MaxPhoneLength })
        {
            throw new DomainValidationException($"Phone cannot exceed {MaxPhoneLength} characters.");
        }

        var normalizedEmail = NormalizeEmail(email);
        var normalizedDni = dni is null ? Dni : NormalizeDni(dni);
        if (hasStructuredNames)
        {
            FirstName = first;
            LastName = last;
        }
        else if (name != FullName)
        {
            FirstName = null;
            LastName = null;
        }

        Email = normalizedEmail;
        FullName = name;
        Phone = normalizedPhone;
        Dni = normalizedDni;
    }

    /// <summary>The first plan starts the monthly membership today; later changes apply from the next month.</summary>
    public void ChangePlan(int planId, DateOnly today)
    {
        PromotePendingPlan(today);

        if (PlanId is null)
        {
            PlanId = planId;
            MonthlyMembershipStartedOn = today;
            return;
        }

        if (PlanId == planId)
        {
            PendingPlanId = null;
            PendingPlanEffectiveFrom = null;
            return;
        }

        PendingPlanId = planId;
        PendingPlanEffectiveFrom = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
    }

    /// <summary>The plan in force on a given day (pending plan changes apply from the first day of the next month).</summary>
    public int? PlanIdOn(DateOnly day) =>
        PendingPlanId is not null && PendingPlanEffectiveFrom is { } from && day >= from ? PendingPlanId : PlanId;

    public void Deactivate() => IsActive = false;

    /// <summary>Links the authcraft identity ("sub"); linking the same identity again is a no-op.</summary>
    public void LinkIdentity(string? identityId)
    {
        var identity = identityId?.Trim();
        if (string.IsNullOrEmpty(identity))
        {
            throw new DomainValidationException("Identity id is required.");
        }

        if (identity.Length > MaxIdentityIdLength)
        {
            throw new DomainValidationException($"Identity id cannot exceed {MaxIdentityIdLength} characters.");
        }

        if (!IsActive)
        {
            throw new ConflictException("Inactive students cannot be linked to an account.");
        }

        if (IdentityId is not null && IdentityId != identity)
        {
            throw new ConflictException("The student is already linked to another account.");
        }

        IdentityId = identity;
    }

    public void SyncRegistrationProfile(StudentProfileSnapshot prefill, StudentRegisteredProfile registered)
    {
        var registeredDni = NormalizeDni(registered.Dni);
        if (Dni is not null && !string.Equals(Dni, registeredDni, StringComparison.Ordinal))
        {
            throw new ConflictException("The registered DNI does not match the student's DNI.");
        }

        var namesMatchSnapshot = string.Equals(FullName, prefill.FullName, StringComparison.Ordinal)
            && ((string.Equals(FirstName, prefill.FirstName, StringComparison.Ordinal)
                 && string.Equals(LastName, prefill.LastName, StringComparison.Ordinal))
                || (FirstName is null && LastName is null));
        var firstName = namesMatchSnapshot ? registered.FirstName : FirstName;
        var lastName = namesMatchSnapshot ? registered.LastName : LastName;
        var fullName = namesMatchSnapshot ? $"{registered.FirstName} {registered.LastName}" : FullName;
        var phone = Phone is null || string.Equals(Phone, prefill.PhoneNumber, StringComparison.Ordinal)
            ? registered.PhoneNumber
            : Phone;

        Update(fullName, Email, phone, Dni ?? registeredDni, firstName, lastName);
    }

    public void SetRoutine(string blobPath, string fileName, string uploadedBy, DateTime uploadedAtUtc)
    {
        RoutineBlobPath = blobPath;
        RoutineFileName = fileName;
        RoutineUploadedBy = uploadedBy;
        RoutineUploadedAt = uploadedAtUtc;
    }

    private void PromotePendingPlan(DateOnly today)
    {
        if (PendingPlanId is not null && PendingPlanEffectiveFrom is { } from && today >= from)
        {
            PlanId = PendingPlanId;
            PendingPlanId = null;
            PendingPlanEffectiveFrom = null;
        }
    }
}
