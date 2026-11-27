namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

public sealed record ParkingVisionOptions(int ReadTimeoutSeconds=40,int ReviewTimeoutSeconds=35,int TotalTimeoutSeconds=75,int OverloadCooldownSeconds=30,bool EnableLocalFallback=true)
{
    internal TimeSpan ReadTimeout=>TimeSpan.FromSeconds(Math.Clamp(ReadTimeoutSeconds,5,60));
    internal TimeSpan ReviewTimeout=>TimeSpan.FromSeconds(Math.Clamp(ReviewTimeoutSeconds,5,45));
    internal TimeSpan TotalTimeout=>TimeSpan.FromSeconds(Math.Clamp(TotalTimeoutSeconds,10,75));
    internal TimeSpan OverloadCooldown=>TimeSpan.FromSeconds(Math.Clamp(OverloadCooldownSeconds,1,120));
}
