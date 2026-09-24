namespace PropFlow.Modules.PropertyAssets.Domain.Buildings;

public class Building
{
    private Building()
    {
        // Parameterless constructor for EF Core
    }

    public Building(
        string code,
        string name,
        string address,
        DateTimeOffset now,
        string timeZoneId = "Asia/Ho_Chi_Minh",
        int numberOfFloors = 1,
        string? description = null,
        Guid? createdBy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        if (numberOfFloors <= 0)
        {
            throw new ArgumentException("Tổng số tầng phải lớn hơn 0.", nameof(numberOfFloors));
        }

        Id = Guid.NewGuid();
        Code = code.Trim();
        Name = name.Trim();
        Address = address.Trim();
        TimeZoneId = ValidateTimeZoneId(timeZoneId);
        NumberOfFloors = numberOfFloors;
        Description = description?.Trim();
        Status = MasterDataStatus.ACTIVE;
        CreatedBy = createdBy;
        UpdatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public string TimeZoneId { get; private set; } = "Asia/Ho_Chi_Minh";
    public int NumberOfFloors { get; private set; } = 1;
    public string? Description { get; private set; }
    public MasterDataStatus Status { get; private set; } = MasterDataStatus.ACTIVE;
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        string name,
        string address,
        string timeZoneId,
        int numberOfFloors,
        string? description,
        Guid? updatedBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        if (numberOfFloors <= 0)
        {
            throw new ArgumentException("Tổng số tầng phải lớn hơn 0.", nameof(numberOfFloors));
        }

        Name = name.Trim();
        Address = address.Trim();
        TimeZoneId = ValidateTimeZoneId(timeZoneId);
        NumberOfFloors = numberOfFloors;
        Description = description?.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Deactivate(Guid? updatedBy, DateTimeOffset now)
    {
        Status = MasterDataStatus.INACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Activate(Guid? updatedBy, DateTimeOffset now)
    {
        Status = MasterDataStatus.ACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    private static string ValidateTimeZoneId(string timeZoneId)
    {
        var normalized = timeZoneId.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalized);
            return normalized;
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ArgumentException("Múi giờ không hợp lệ.", nameof(timeZoneId), exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ArgumentException("Múi giờ không hợp lệ.", nameof(timeZoneId), exception);
        }
    }
}
