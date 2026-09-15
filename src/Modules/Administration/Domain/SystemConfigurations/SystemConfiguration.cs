namespace PropFlow.Modules.Administration.Domain.SystemConfigurations;

public class SystemConfiguration
{
    private SystemConfiguration()
    {
    }

    public SystemConfiguration(
        string configKey,
        string configValue,
        DateTimeOffset now,
        ConfigValueType valueType = ConfigValueType.STRING,
        string? description = null,
        bool isActive = true,
        Guid? updatedBy = null)
    {
        if (string.IsNullOrWhiteSpace(configKey))
            throw new ArgumentException("Config key is required.", nameof(configKey));

        Id = Guid.NewGuid();
        ConfigKey = configKey.Trim();
        ConfigValue = configValue ?? string.Empty;
        ValueType = valueType;
        Description = description?.Trim();
        IsActive = isActive;
        UpdatedBy = updatedBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string ConfigKey { get; private set; } = string.Empty;
    public string ConfigValue { get; private set; } = string.Empty;
    public ConfigValueType ValueType { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateValue(string newValue, Guid? updatedBy, DateTimeOffset now)
    {
        ConfigValue = newValue ?? string.Empty;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void ToggleActive(bool isActive, Guid? updatedBy, DateTimeOffset now)
    {
        IsActive = isActive;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
