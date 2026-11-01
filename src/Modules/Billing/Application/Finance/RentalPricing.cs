namespace PropFlow.Modules.Billing.Application.Finance;

public sealed record RentalPricingInput(Guid ApartmentUnitId, string Basis, decimal UnitRate, DateOnly EffectiveFrom, Guid? ApartmentTypeId = null);
public sealed record RentalPricingView(Guid Id, Guid ApartmentUnitId, string Basis, decimal UnitRate, DateOnly EffectiveFrom, DateOnly? EffectiveTo, Guid? ApartmentTypeId = null);
public interface IRentalPricing
{
    Task<IReadOnlyList<RentalPricingView>> ListAsync(Guid? apartmentId, CancellationToken ct);
    Task<IReadOnlyList<RentalPricingView>> ListForTypeAsync(Guid typeId, CancellationToken ct);
    Task SaveAsync(RentalPricingInput input, Guid actor, CancellationToken ct);
}
