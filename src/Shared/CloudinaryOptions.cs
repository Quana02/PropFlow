namespace PropFlow.Shared;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string ServiceRequestFolder { get; set; } = "propflow/service-request-evidence";
    public string MaintenanceFolder { get; set; } = "propflow/maintenance-evidence";
}
