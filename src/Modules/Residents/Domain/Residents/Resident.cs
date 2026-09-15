using PropFlow.Modules.Residents.Domain.ResidentApartments;

namespace PropFlow.Modules.Residents.Domain.Residents;

public class Resident
{
    private readonly List<ResidentApartment> _residentApartments = [];

    private Resident()
    {
        // Parameterless constructor for EF Core
    }

    public Resident(
        string residentCode,
        string fullName,
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
        UserId = userId;
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
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

    public void UpdateProfile(string fullName, string? phoneNumber, string? email, string? note, Guid? updatedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        FullName = fullName.Trim();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Note = note?.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

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
}
