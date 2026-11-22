using Microsoft.AspNetCore.Components;

namespace PropFlow.UnitTests;

internal sealed class FacilityTestNavigation : NavigationManager
{
    public int Visits { get; private set; }
    public FacilityTestNavigation() => Initialize("https://facility.test/", "https://facility.test/resident/facilities");
    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        Visits++; Uri = ToAbsoluteUri(uri).ToString(); NotifyLocationChanged(false);
    }
}
