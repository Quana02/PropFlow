using PropFlow.Web.Client.Features.Apartment.Models;

namespace PropFlow.Web.Client.Features.Apartment.Pages;

public partial class ApartmentManagement
{
    private ApartmentMeasurements defaultBaseline = new(null, null, null);
    private ApartmentUnitTypeResponse? pendingType;
    private int typeSelectionVersion;
    private ApartmentMeasurements Measurements => new(form.UsableAreaM2, form.BedroomCount, form.BathroomCount);

    private Task SetFormUnitType(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == form.ApartmentUnitTypeId) return Task.CompletedTask;
        // Editing an existing apartment keeps its actual measurements, including on type changes.
        if (editId.HasValue) { form.ApartmentUnitTypeId = id; return Task.CompletedTask; }
        var next = apartmentTypes.SingleOrDefault(x => x.Id == id);
        if (next is null) return Task.CompletedTask;
        var invalidManualArea = !string.IsNullOrWhiteSpace(areaText) && form.UsableAreaM2 is null && next.DefaultUsableAreaM2.HasValue;
        if (invalidManualArea || ApartmentDefaultSelection.NeedsConfirmation(Measurements, defaultBaseline, next)) pendingType = next;
        else ApplyType(next);
        return Task.CompletedTask;
    }

    private void ApplyType(ApartmentUnitTypeResponse next)
    {
        var values = ApartmentDefaultSelection.Apply(Measurements, next);
        form.ApartmentUnitTypeId = next.Id;
        form.UsableAreaM2 = values.Area;
        form.BedroomCount = values.Bedrooms;
        form.BathroomCount = values.Bathrooms;
        if (next.DefaultUsableAreaM2.HasValue) areaText = ApartmentAreaInput.Format(values.Area);
        defaultBaseline = ApartmentDefaultSelection.Apply(defaultBaseline, next);
        pendingType = null;
    }

    private void ConfirmTypeChange() { if (pendingType is { } next) ApplyType(next); }
    private void CancelTypeChange() { pendingType = null; typeSelectionVersion++; }
    private Task ReloadTypes() => LoadUnitTypes(true);
    private async Task TypesChanged() { await LoadUnitTypes(true); await Load(); }
}
