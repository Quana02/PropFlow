using PropFlow.Modules.Residents.Domain.ResidentApartments;

namespace PropFlow.Modules.Residents.Domain.Residents;

public class Resident
{
    private readonly List<ResidentApartment> _residentApartments = [];

    private Resident()
    {
        // Parameterless constructor for EF Core
    }

    // Compatibility overload for historical migrations/tests and existing callers. New onboarding uses the full profile constructor.
    public Resident(string residentCode, string fullName, DateTimeOffset now, Guid? userId = null, string? phoneNumber = null, string? email = null, string? note = null, Guid? createdBy = null)
        : this(residentCode, fullName, null, null, null, null, null, null, null, now, userId, phoneNumber, email, note, createdBy)
    {
    }

    public Resident(
        string residentCode,
        string fullName,
        DateOnly? dateOfBirth,
        string? gender,
        string? nationality,
        string? identityType,
        string? identityNumber,
        DateOnly? identityIssuedDate,
        DateOnly? identityExpiryDate,
        DateTimeOffset now,
        Guid? userId = null,
        string? phoneNumber = null,
        string? email = null,
        string? note = null,
        Guid? createdBy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(residentCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        Id = Guid.NewGuid();
        ResidentCode = residentCode.Trim();
        FullName = fullName.Trim();
        DateOfBirth = dateOfBirth;
        Gender = Normalize(gender, 30);
        Nationality = Normalize(nationality, 80);
        IdentityType = ResidentProfileNormalization.IdentityType(identityType);
        IdentityNumber = ResidentProfileNormalization.IdentityNumber(identityNumber);
        IdentityIssuedDate = identityIssuedDate;
        IdentityExpiryDate = identityExpiryDate;
        UserId = userId;
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        Email = ResidentProfileNormalization.Email(email);
        Status = ResidentStatus.ACTIVE;
        Note = note?.Trim();
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    // Cross-module scalar ID to auth.users.id (nullable: business resident profile may exist before login account is linked)
    public Guid? UserId { get; private set; }

    public string ResidentCode { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public DateOnly? DateOfBirth { get; private set; }
    public string? Gender { get; private set; }
    public string? Nationality { get; private set; }
    public string? IdentityType { get; private set; }
    public string? IdentityNumber { get; private set; }
    public DateOnly? IdentityIssuedDate { get; private set; }
    public DateOnly? IdentityExpiryDate { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? Email { get; private set; }
    public ResidentStatus Status { get; private set; } = ResidentStatus.ACTIVE;
    public string? Note { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Within-module relationship
    public IReadOnlyCollection<ResidentApartment> ResidentApartments => _residentApartments.AsReadOnly();

    public void LinkUserAccount(Guid userId, Guid? updatedBy, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));
        }

        UserId = userId;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void UpdateProfile(string fullName, DateOnly? dateOfBirth, string? gender, string? nationality, string? identityType, string? identityNumber, DateOnly? identityIssuedDate, DateOnly? identityExpiryDate, string? phoneNumber, string? email, string? note, Guid? updatedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        FullName = fullName.Trim();
        DateOfBirth = dateOfBirth;
        Gender = Normalize(gender, 30);
        Nationality = Normalize(nationality, 80);
        IdentityType = ResidentProfileNormalization.IdentityType(identityType);
        IdentityNumber = ResidentProfileNormalization.IdentityNumber(identityNumber);
        IdentityIssuedDate = identityIssuedDate;
        IdentityExpiryDate = identityExpiryDate;
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        Email = ResidentProfileNormalization.Email(email);
        Note = note?.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void UpdateProfile(string fullName, string? phoneNumber, string? email, string? note, Guid? updatedBy, DateTimeOffset now)
        => UpdateProfile(fullName, DateOfBirth, Gender, Nationality, IdentityType, IdentityNumber, IdentityIssuedDate, IdentityExpiryDate, phoneNumber, email, note, updatedBy, now);

    public void Activate(Guid? updatedBy, DateTimeOffset now)
    {
        Status = ResidentStatus.ACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Deactivate(Guid? updatedBy, DateTimeOffset now)
    {
        Status = ResidentStatus.INACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void MarkMovedOut(Guid? updatedBy, DateTimeOffset now)
    {
        Status = ResidentStatus.MOVED_OUT;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength) throw new ArgumentException($"Value cannot exceed {maxLength} characters.");
        return normalized;
    }
}
