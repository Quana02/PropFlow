namespace PropFlow.Modules.Billing.Contracts;

public sealed record BillingHousehold(Guid ApartmentId, string UnitNumber, decimal AreaM2,
    Guid UserId, string FullName, string? Email, Guid? ResidentId = null, string Capacity = "HOUSEHOLD_HEAD",
    DateOnly? RentalStart = null, DateOnly? RentalEnd = null, Guid? ApartmentTypeId = null);

public interface IBillingHouseholdDirectory
{
    Task<IReadOnlyDictionary<Guid, Guid>> LinkedAccountsAsync(IReadOnlyCollection<Guid> residentIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(new Dictionary<Guid, Guid>());
    Task<string?> RecipientEmailAsync(Guid? userId, string? snapshotEmail, CancellationToken ct) => Task.FromResult(snapshotEmail);
    async Task<IReadOnlyList<BillingApartment>> ManagedApartmentsAsync(CancellationToken ct) =>
        (await ListChargeableAsync(DateOnly.FromDateTime(DateTime.UtcNow), ct)).Select(x => new BillingApartment(x.ApartmentId, x.UnitNumber)).Distinct().ToArray();
    Task<IReadOnlyList<BillingHousehold>> ListAsync(DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<BillingHousehold>> ListChargeableAsync(DateOnly date, CancellationToken ct) => ListAsync(date, ct);
    async Task<BillingHousehold?> DescribeAsync(Guid apartmentId, DateOnly date, CancellationToken ct)
    {
        var matches = (await ListAsync(date, ct)).Where(x => x.ApartmentId == apartmentId).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
public sealed record BillingApartment(Guid Id, string UnitNumber, Guid? ApartmentTypeId = null, string? ApartmentTypeName = null);

public sealed record FinanceNotice(Guid EventId, Guid? UserId, string? Email, string Title,
    string Message, string Html, string SourceType, Guid SourceId, string ActionPath, string? AttachmentFileName = null, Guid? PreviousRecipientEventId = null);

public interface IFinanceNoticePublisher
{
    Task PublishAsync(FinanceNotice notice, CancellationToken ct);
}
